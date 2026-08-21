<#
.SYNOPSIS
    Stands up a real Ansible controller and two managed nodes in Docker and runs the collection
    against them over SSH.

.DESCRIPTION
    The collection's own suites cannot reach this far. `ansible-test units` exercises the plugins as
    Python, and `ansible-test integration` runs its targets on localhost -- neither one proves that
    the module renders a *remote* node's files using a binary installed on that node, which is the
    whole reason the module exists. This rig does: two nodes, different inventory variables, a real
    sshd, and the collection installed from Galaxy exactly as an operator would install it.

    Nothing here runs in CI. It needs a Docker daemon and pulls a ~1 GB base image, so it is a
    harness a maintainer runs deliberately -- before a release, or when changing the module's
    contract with the node.

.PARAMETER Command
    up     build the images and start the containers
    test   run the playbooks against a running rig
    down   remove the containers and the network
    all    down, then up, then test (the default)

.PARAMETER Version
    The tool version to install on the nodes. Defaults to the <Version> in Directory.Build.props, so
    by default the rig tests the version this working tree declares.

.PARAMETER PackageSource
    published  download the release from nuget.org -- tests what an operator actually receives
    local      dotnet pack this working tree -- tests changes that are not released yet

.PARAMETER Collection
    What to hand `ansible-galaxy collection install`. Defaults to the published collection. Point it
    at a built tarball to test an unreleased collection.

.EXAMPLE
    ./rig.ps1
    Full cycle against the published tool and the published collection.

.EXAMPLE
    ./rig.ps1 -PackageSource local -Collection ../../ansible/stop_cran-namespace2xml-2.4.0.tar.gz
    Full cycle against this working tree's tool and a locally built collection.

.EXAMPLE
    ./rig.ps1 -Command test
    Re-run the playbooks against a rig that is already up. Running this twice is the idempotence
    check: playbooks 01, 03 and 04 must report changed=0 on the second pass.
#>
[CmdletBinding()]
param(
    [ValidateSet('up', 'test', 'down', 'all')]
    [string] $Command = 'all',

    [string] $Version,

    [ValidateSet('published', 'local')]
    [string] $PackageSource = 'published',

    [string] $Collection = 'stop_cran.namespace2xml'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'

$rig = $PSScriptRoot
$repo = Resolve-Path (Join-Path (Join-Path $rig '..') '..')
$keys = Join-Path $rig '.keys'
$pkg = Join-Path $rig 'pkg'
$network = 'n2x-net'
$controller = 'n2x-ctl'
$nodes = @('n2x-node1', 'n2x-node2')

# Docker and git both write ordinary progress to stderr, which PowerShell turns into a terminating
# NativeCommandError under $ErrorActionPreference='Stop'. Judge these by exit code, never by stream.
function Invoke-Native {
    param([string] $What, [scriptblock] $Action, [switch] $IgnoreFailure)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Action 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $previous }

    if ($LASTEXITCODE -ne 0 -and -not $IgnoreFailure) {
        throw "$What failed with exit code $LASTEXITCODE."
    }
}

function Get-DeclaredVersion {
    $props = Join-Path $repo 'Directory.Build.props'
    if (-not (Test-Path -LiteralPath $props)) {
        throw "Cannot default the version: $props does not exist. Pass -Version explicitly."
    }

    $match = [regex]::Match((Get-Content -LiteralPath $props -Raw), '<Version>([^<]+)</Version>')
    if (-not $match.Success) {
        throw "Cannot default the version: $props declares no <Version>. Pass -Version explicitly."
    }

    $match.Groups[1].Value.Trim()
}

function Assert-Prerequisites {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'docker is not on PATH. This rig needs a running Docker daemon.'
    }

    Invoke-Native 'docker info' { docker info --format '{{.ServerVersion}}' } | Out-Null

    if (-not (Get-Command ssh-keygen -ErrorAction SilentlyContinue)) {
        throw 'ssh-keygen is not on PATH. It ships with OpenSSH, which is present by default on Windows 10+, macOS and every mainstream Linux.'
    }
}

function Test-UnencryptedKey {
    param([string] $Path)

    # An OpenSSH private key carries its cipher name inside the base64 body: `none` when there is no
    # passphrase, `aes256-ctr` when there is. The PEM-era `Proc-Type: 4,ENCRYPTED` header is absent
    # from both, so grepping for `ENCRYPTED` would pass an encrypted key silently.
    if (-not (Test-Path -LiteralPath $Path)) { return $false }

    try {
        $body = (Get-Content -LiteralPath $Path -Raw) -split "`n" |
            Where-Object { $_ -notmatch '^-----' }
        $bytes = [Convert]::FromBase64String((($body -join '').Trim()))
        $prefix = [Text.Encoding]::ASCII.GetString($bytes[0..47])
        return $prefix -match 'openssh-key-v1\x00\x00\x00\x00\x04none'
    }
    catch { return $false }
}

function New-RigKeys {
    # A throwaway keypair generated per run. Committing one would put a private key in git, and
    # reusing one across runs would leave a credential lying around for containers that are gone.
    if (Test-Path -LiteralPath $keys) { Remove-Item -LiteralPath $keys -Recurse -Force }
    New-Item -ItemType Directory -Path $keys -Force | Out-Null

    $private = Join-Path $keys 'id_rig'

    # Windows PowerShell 5.1 drops a genuinely empty argument, so `-N ''` makes ssh-keygen read the
    # next flag as the passphrase and fail; `-N '""'` is what reaches it as empty. PowerShell 7
    # passes '' correctly and would take '""' as a two-character passphrase -- an encrypted key,
    # which then cannot authenticate. Rather than branch on the edition, try both and verify the
    # artefact: the check below is what actually decides, so a future edition cannot break this
    # quietly.
    foreach ($spelling in @('""', '')) {
        Remove-Item -LiteralPath $private, "$private.pub" -Force -ErrorAction SilentlyContinue
        Invoke-Native 'ssh-keygen' {
            ssh-keygen -t ed25519 -N $spelling -C 'namespace2xml-integration-rig' -f $private -q
        } -IgnoreFailure | Out-Null

        if ((Test-Path -LiteralPath "$private.pub") -and (Test-UnencryptedKey -Path $private)) {
            # authorized_keys is read by sshd inside a Linux container, so it must be LF whatever
            # this host writes by default.
            $public = [IO.File]::ReadAllText("$private.pub").Replace("`r`n", "`n").TrimEnd("`n")
            [IO.File]::WriteAllText((Join-Path $keys 'authorized_keys'), $public + "`n")
            Write-Host '  keypair generated in .keys/ (gitignored)'
            return
        }
    }

    throw 'ssh-keygen produced no usable passphrase-less key. Generate one manually into .keys/id_rig and re-run.'
}

function Get-ToolPackage {
    param([string] $ToolVersion)

    if (Test-Path -LiteralPath $pkg) { Remove-Item -LiteralPath $pkg -Recurse -Force }
    New-Item -ItemType Directory -Path $pkg -Force | Out-Null

    if ($PackageSource -eq 'local') {
        Write-Host "  packing $ToolVersion from this working tree"
        Push-Location $repo
        try {
            Invoke-Native 'dotnet pack' { dotnet pack src/Namespace2Xml.Cli/Namespace2Xml.Cli.csproj -c Release -o $pkg } | Out-Null
        }
        finally { Pop-Location }
    }
    else {
        # The V2 endpoint, not api.nuget.org: the V3 index resolves IPv6-only on some networks and
        # the default Docker bridge has no IPv6 route, so a container cannot restore from it.
        $url = "https://www.nuget.org/api/v2/package/namespace2xml/$ToolVersion"
        $destination = Join-Path $pkg "namespace2xml.$ToolVersion.nupkg"
        Write-Host "  downloading $url"
        Invoke-WebRequest -Uri $url -OutFile $destination -UseBasicParsing -TimeoutSec 300
    }

    $packages = @(Get-ChildItem -LiteralPath $pkg -Filter 'namespace2xml.*.nupkg')
    if ($packages.Count -eq 0) {
        throw "No package landed in $pkg. The rig cannot install a tool it does not have."
    }

    $packages | ForEach-Object { Write-Host ("  {0}  {1:N0} bytes" -f $_.Name, $_.Length) }
}

function Invoke-Up {
    param([string] $ToolVersion)

    Write-Host "== preparing ($PackageSource, tool $ToolVersion, collection $Collection)"
    New-RigKeys
    Get-ToolPackage -ToolVersion $ToolVersion

    Write-Host '== building images'
    Push-Location $rig
    try {
        Invoke-Native 'docker build (node)' {
            docker build -f Dockerfile.node --build-arg "TOOL_VERSION=$ToolVersion" -t n2x-node:rig .
        } | Select-Object -Last 3
        Invoke-Native 'docker build (controller)' {
            docker build -f Dockerfile.controller --build-arg "TOOL_VERSION=$ToolVersion" --build-arg "COLLECTION_SPEC=$Collection" -t n2x-ctl:rig .
        } | Select-Object -Last 3
    }
    finally { Pop-Location }

    Write-Host '== starting containers'
    Invoke-Native 'docker network create' { docker network create $network } -IgnoreFailure | Out-Null
    foreach ($node in $nodes) {
        Invoke-Native "docker run $node" { docker run -d --name $node --network $network --hostname $node n2x-node:rig } | Out-Null
    }
    Invoke-Native "docker run $controller" {
        docker run -d --name $controller --network $network --hostname $controller -v "${rig}:/play" n2x-ctl:rig sleep infinity
    } | Out-Null

    Start-Sleep -Seconds 5
    Write-Host '== the rig'
    Invoke-Native 'docker ps' { docker ps --filter 'name=n2x-' --format '  {{.Names}}  {{.Image}}  {{.Status}}' }

    # An unreachable node makes every later failure ambiguous, so prove SSH before running anything.
    Write-Host '== connectivity'
    $ping = Invoke-Exec 'ansible nodes -m ping'
    if (($ping -join "`n") -notmatch 'node1 \| SUCCESS' -or ($ping -join "`n") -notmatch 'node2 \| SUCCESS') {
        $ping | ForEach-Object { Write-Host "  $_" }
        throw 'Both nodes must answer ping before the suite can mean anything.'
    }
    Write-Host '  node1 and node2 both answered'
}

function Invoke-Exec {
    param([string] $CommandLine)

    # ANSIBLE_CONFIG explicitly: /play is a bind mount, Ansible sees it as world-writable and
    # silently ignores the ansible.cfg sitting in it, which would take the inventory with it.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        docker exec -e ANSIBLE_CONFIG=/play/ansible.cfg $controller bash -lc "cd /play && $CommandLine" 2>&1 | ForEach-Object { "$_" }
    }
    finally { $ErrorActionPreference = $previous }
}

function Invoke-Test {
    $playbooks = @(Get-ChildItem -LiteralPath (Join-Path $rig 'playbooks') -Filter '*.yml' | Sort-Object Name)
    if ($playbooks.Count -eq 0) { throw 'No playbooks found; there is nothing to run.' }

    $results = @(foreach ($playbook in $playbooks) {
        Write-Host ''
        Write-Host "==================== $($playbook.Name) ===================="
        $output = Invoke-Exec "ansible-playbook playbooks/$($playbook.Name)"

        $recap = $false
        foreach ($line in $output) {
            if ($line -match 'PLAY RECAP') { $recap = $true }
            if ($recap) { Write-Host $line }
        }

        # `failed=N` in the recap, not the exit code: a play that deliberately ignores an expected
        # failure still exits non-zero in some ansible-core versions.
        $failures = @($output | Where-Object { $_ -match 'failed=[1-9]' })
        $status = if ($failures.Count -eq 0 -and $recap) { 'PASS' } else { 'FAIL' }
        if (-not $recap) { $output | ForEach-Object { Write-Host $_ } }
        Write-Host "  -> $status"

        [pscustomobject]@{ Playbook = $playbook.Name; Status = $status }
    })

    Write-Host ''
    Write-Host '==================== SUMMARY ===================='
    $results | ForEach-Object { Write-Host ("  {0,-26} {1}" -f $_.Playbook, $_.Status) }
    $failed = @($results | Where-Object Status -eq 'FAIL').Count
    Write-Host ''
    Write-Host "  $($results.Count) playbooks, $failed failing"

    if ($failed -gt 0) { throw "$failed playbook(s) failed." }
}

function Invoke-Down {
    Write-Host '== tearing down'
    # By name only. This host may carry unrelated containers, so no prune, ever.
    foreach ($container in @($controller) + $nodes) {
        Invoke-Native "docker rm $container" { docker rm -f $container } -IgnoreFailure | Out-Null
    }
    Invoke-Native 'docker network rm' { docker network rm $network } -IgnoreFailure | Out-Null
    Write-Host '  containers and network removed'
}

Assert-Prerequisites
if (-not $Version) { $Version = Get-DeclaredVersion }

switch ($Command) {
    'up' { Invoke-Up -ToolVersion $Version }
    'test' { Invoke-Test }
    'down' { Invoke-Down }
    'all' {
        Invoke-Down
        Invoke-Up -ToolVersion $Version
        Invoke-Test
    }
}
