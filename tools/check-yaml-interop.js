// Verify Section 19.4's emitted YAML against an independent YAML 1.2 reader.
//
// Section 19.4 quotes a scalar that either published YAML schema would type. The Python lane
// covers YAML 1.1 through PyYAML; this one covers YAML 1.2 through js-yaml, and the two are
// not interchangeable. A 1.1 reader types `yes`, `12:30`, `2001-12-14` and `1_000` and leaves
// `0o17` a string; a 1.2 reader does the reverse. Either lane alone would pass a writer that
// picked one revision and left the other's readers wrong, which is the whole reason Section
// 19.4 takes the union rather than a side.
//
// The oracle is the corpus, not the implementation. For a case whose input is JSON and whose
// scheme only names an output format, the emitted YAML must carry exactly the document the
// JSON carried: JSON.parse fixes the types with no YAML resolution involved, so comparing the
// two establishes that a third-party reader gives the values back rather than merely
// accepting the file.
//
// Nothing here imports the implementation, and nothing here re-implements Section 19.4's
// spelling rules.
//
// Usage:  node tools/check-yaml-interop.js [repo-root]

'use strict';

const fs = require('fs');
const path = require('path');

let yaml;
try {
  yaml = require('js-yaml');
} catch (error) {
  process.stderr.write('check-yaml-interop: js-yaml is not installed\n');
  process.exit(2);
}

// Cases whose emitted bytes carry the spellings the two YAML revisions disagree about. The
// lane is nearly vacuous without them, so their absence from the checked set is a failure
// rather than a skip.
const REQUIRED_CASES = [
  'yaml-quotes-every-portably-typed-spelling',
  'yaml-scalar-style-selection',
];

// The shared selector an output-only scheme names, or null when it names more. A scheme that
// filters or transforms makes the emitted document a function of rules this lane does not
// model. One shared selector is modelled: the emitted document is then the subtree the JSON
// carries at that path.
function schemePrefix(text) {
  const prefixes = new Set();
  for (const raw of text.split('\n')) {
    const line = raw.trim();
    if (line === '' || line.startsWith('#')) {
      continue;
    }
    const selector = line.split('=')[0].trim();
    const cut = selector.lastIndexOf('.');
    const head = cut === -1 ? '' : selector.slice(0, cut);
    const name = cut === -1 ? selector : selector.slice(cut + 1);
    if (name.toLowerCase() !== 'output' && name.toLowerCase() !== 'filename') {
      return null;
    }
    prefixes.add(head);
  }
  if (prefixes.size !== 1) {
    return null;
  }
  const only = [...prefixes][0];
  return only === '' ? [] : only.split('.');
}

function listFiles(dir, extension) {
  if (!fs.existsSync(dir)) {
    return [];
  }
  return fs.readdirSync(dir).filter((name) => name.endsWith(extension)).sort()
    .map((name) => path.join(dir, name));
}

// Structural equality over the shapes JSON and YAML share. Key order is not compared: the
// corpus pins order in bytes, and this lane is about values surviving a reader.
function same(left, right) {
  if (left === null || right === null) {
    return left === right;
  }
  if (Array.isArray(left) || Array.isArray(right)) {
    return Array.isArray(left) && Array.isArray(right)
      && left.length === right.length
      && left.every((item, index) => same(item, right[index]));
  }
  if (typeof left === 'object' || typeof right === 'object') {
    if (typeof left !== 'object' || typeof right !== 'object') {
      return false;
    }
    const leftKeys = Object.keys(left).sort();
    const rightKeys = Object.keys(right).sort();
    return leftKeys.length === rightKeys.length
      && leftKeys.every((key, index) => key === rightKeys[index])
      && leftKeys.every((key) => same(left[key], right[key]));
  }
  return Object.is(left, right);
}

// A key a 1.2 reader did not give back as a string. js-yaml stringifies object keys, so a
// resolved Boolean or number arrives as its canonical text rather than the written spelling;
// comparing the two is what exposes the collapse.
function nonStringKeys(node, written, found) {
  if (node === null || typeof node !== 'object') {
    return;
  }
  if (Array.isArray(node)) {
    node.forEach((item) => nonStringKeys(item, written, found));
    return;
  }
  for (const key of Object.keys(node)) {
    if (!written.has(key)) {
      found.push(key);
    }
    nonStringKeys(node[key], written, found);
  }
}

function main(argv) {
  const root = path.resolve(argv[2] || '.');
  const conformance = path.join(root, 'conformance');
  const failures = [];
  const checked = new Set();
  let loaded = 0;

  for (const caseName of fs.readdirSync(conformance).sort()) {
    const caseDir = path.join(conformance, caseName);
    if (!fs.statSync(caseDir).isDirectory()) {
      continue;
    }

    for (const emitted of listFiles(path.join(caseDir, 'expected'), '.yaml')) {
      loaded += 1;
      try {
        yaml.load(fs.readFileSync(emitted, 'utf8'));
      } catch (error) {
        failures.push(`${path.relative(root, emitted)}: a YAML 1.2 reader refuses the document -- ${error.message.split('\n')[0]}`);
      }
    }

    const inputs = listFiles(path.join(caseDir, 'inputs'), '.json');
    const emittedYaml = listFiles(path.join(caseDir, 'expected'), '.yaml');
    const schemes = listFiles(path.join(caseDir, 'schemes'), '.txt');
    if (inputs.length !== 1 || emittedYaml.length !== 1 || schemes.length !== 1) {
      continue;
    }
    const prefix = schemePrefix(fs.readFileSync(schemes[0], 'utf8'));
    if (prefix === null) {
      continue;
    }

    let expected = JSON.parse(fs.readFileSync(inputs[0], 'utf8'));
    let reachable = true;
    for (const component of prefix) {
      if (expected === null || typeof expected !== 'object' || !(component in expected)) {
        reachable = false;
        break;
      }
      expected = expected[component];
    }
    if (!reachable) {
      continue;
    }

    let actual;
    try {
      actual = yaml.load(fs.readFileSync(emittedYaml[0], 'utf8'));
    } catch (error) {
      checked.add(caseName);
      continue;
    }

    if (!same(actual, expected)) {
      failures.push(`${caseName}: '${path.basename(emittedYaml[0])}' does not read back as the JSON it was built from\n`
        + `      json: ${JSON.stringify(expected)}\n      yaml: ${JSON.stringify(actual)}`);
    }

    const written = new Set();
    (function collect(node) {
      if (node === null || typeof node !== 'object') {
        return;
      }
      if (Array.isArray(node)) {
        node.forEach(collect);
        return;
      }
      Object.keys(node).forEach((key) => { written.add(key); collect(node[key]); });
    }(expected));

    const strayKeys = [];
    nonStringKeys(actual, written, strayKeys);
    for (const key of strayKeys) {
      failures.push(`${caseName}: key '${key}' is not a key the JSON wrote, so a reader resolved it away from its spelling`);
    }

    checked.add(caseName);
  }

  const missing = REQUIRED_CASES.filter((name) => !checked.has(name));
  if (missing.length > 0) {
    failures.push(`the cases this lane exists for were not compared: ${missing.sort().join(', ')}`);
  }

  if (failures.length > 0) {
    process.stderr.write('check-yaml-interop: FAILED\n');
    for (const failure of failures) {
      process.stderr.write(`  - ${failure}\n`);
    }
    return 1;
  }

  process.stdout.write(`check-yaml-interop: ${loaded} emitted YAML files load under js-yaml; `
    + `${checked.size} compared to the JSON they were built from\n`);
  return 0;
}

process.exit(main(process.argv));
