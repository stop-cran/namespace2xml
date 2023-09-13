# Overview [![NuGet](https://img.shields.io/nuget/v/namespace2xml.svg)](https://www.nuget.org/packages/namespace2xml) [![Build Status](https://travis-ci.com/stop-cran/namespace2xml.svg?branch=master)](https://travis-ci.com/stop-cran/namespace2xml) [![Coverage Status](https://coveralls.io/repos/github/stop-cran/namespace2xml/badge.svg?branch=master)](https://coveralls.io/github/stop-cran/namespace2xml?branch=master)

Namespace2xml is a tool for generating, templating and merging files in following formats:
 - XML
 - JSON
 - YAML
 - INI
 - Bash variables

The tool is primarily intended to deal with various configuration files. Here its basic features:

Basic usage:

```
namespace2xml -i <a set of input files> -s <a set of scheme files>
```
Here scheme files describe format and details of output files - format, XML element or attribute etc.

## Store all configuration in a format-agnostic mode

Input file comprises a set of key-value pairs with namespaces (referred below as a *profile*):
```
some.namespace.key=value
some.another.namespace.another-key=another-value
```
 
## Generate configuration files of various formats

Input profile (test.properties):
```
a.b.x=1
```
Scheme (scheme.properties):
```
a.output=xml,json,yaml,ini,namespace
```
Call:
```
namespace2xml -i test.properties -s scheme.properties
```
Output files:
a.json:
```
{
  "b": {
    "x": 1
  }
}
```
a.yml:
```
b:
  x: 1
  ```
a.ini:
```
[b]
x = 1
```
a.properties (another profile):
```
b.x=1
```
a.xml (root `a` remains here since there should be a single root XML element):
```
<a>
  <b x="1" />
</a>
```

## Merge data from multiple sources

When one have more than one source of configuration, merging them into a single file can be awkward. Often it's done by introducing templates like JSON-with-dollars `{ "a": $MY_VAR }` and processing them with sed and a bunch of environment variables.
In general, merging two XML, JSON or YAML files into a single one is a non-trivial job and many solutions are non-composable, i.e. when merging three files the order matters, `(a+b)+c` is not the same as `a+(b+c)`. This causes further difficulties in complex scenarios.
On contrary, merging two profiles is straightforward - actually, it doesn't matter if one passes multiple input files to the namespace2xml tool, or concatenate all of them into a single file (delimiting with newlines) and then pass this single file.
The value overriding is performed from the beginnging to the end, giving priority to later values. It is logged if `--verbosity debug` command-line option is passed to the tool.
 
## Support for value references and wildcard substitutes

### Reference one value from another:
Example:

*Input*
```
a.x=1,${b.y},3
b.y=2
```
*Schema*
```
a.output=namespace
```
*Output*
```
x=1,2,3
```

### Substitutions:
Example:

*Input*
```
a.x=1
a.y=2
a.*.z=3
```
*Schema*
```
output=namespace
```
*Output*
```
a.x=1
a.y=2
a.x.z=3
a.y.z=3
```
\
Example:

*Input*
```
a.x=1
a.*.y=*
a.*.*.z=*.*
```
*Schema*
```
output=namespace
```
*Output*
```
a.x=1
a.x.y=x
a.x.y.z=x.y
```
\
Example:

*Input*
```
a.x.y=1
a.*.*=*.*.*.*
```
*Schema*
```
output=namespace
```
*Output*
```
a.x.y=x.y.y.y
```

### Reference with substitutions:
Example:

*Input*
```
a.b.x=1
a.b.*=${c.*}
c.x=2
```
*Schema*
```
a.output=namespace
```
*Output*
```
b.x=2
```
\
All use-cases above can be combined and used simultaneously.

Example:

*Input*
```
a.b.x=1
a.b.*=2,${c.*},${d.*}
c.x=3
d.x=4
```
*Schema*
```
a.output=namespace
```
*Output*
```
b.x=2,3,4
```
\
Example

*Input*
```
a.x.y=1
a.*.*=1,${b.*},${c.*}
b.x=2
c.x=3
```
*Schema*
```
a.output=namespace
```
*Output*
```
x.y=1,2,3
```
\
Example:

*Input*
```
a.x.y=1
a.*.*=1,*,${c.*},*
c.x=3
```
*Schema*
```
a.output=namespace
```
*Output*
```
x.y=1,x,3,y
```

### Ignore
Example:

*Input*
```
a.b=1
!x.y=2
```
*Schema*
```
output=namespace
```
*Output*
```
a.b=1
```

It is possible to use wildcards with `!`.

Example:

*Input*
```
a.b.c=1
a.b.d=2
a.b.e=3
a.x.y=4
a.x.z=5
!a.x.*
```
*Schema*
```
output=namespace
```
*Output*
```
a.b.c=1
a.b.d=2
a.b.e=3
```

### Delimiter escape
Example:

*Input*
```
a.b\.c=1
```
*Schema*
```
output=yaml
```
*Output*
```yaml
a:
  b.c: 1
```

# Scheme description

## Output

### Supported output types
```
[name.]*output=[xml|json|yaml|ini|namespace|quotednamespace]
```

### Full output
Example:

*Input*
```
a.b.x=1
a.b.y=2
a.c=3
x.y=4
```
*Scheme*
```
output=namespace
```
*Output*
```
a.b.x=1
a.b.y=2
a.c=3
x.y=4
```

### Partial output
Example:

*Input*
```
a.b.x=1
a.b.y=2
a.c=3
x.y=4
```
*Scheme*
```
a.output=namespace
```
*Output*
```
b.x=1
b.y=2
c=3
```

## Set output file name or path
```
[name.]*filename=<overridden file name>
```

It is possible to write multiple output to a single file. The content will be added in case of `namespace` and `yaml` output types, otherwise it will be overriden.

## Change name of the root element(s)/namespace/INI section name
```
[name.]*root=<name, maybe hierarchical, separated by dots>
```
For all direct child elements treat child entry name as a key and group grand-children by that key:
```
[name.]*root=<a name of XML attribute or YAML/JSON property name used to write the value of the key>
```

Example:

*Input*
```
a.b.x=1
a.b.y=2
```
*Scheme*
```
a.output=namespace
a.root=c
```
*Output*
```
c.b.x=1
c.b.y=2
```

## Keys
Treat all elements in given namespace as a dictionary with given key attribute.

Example:

*Input*
```
a.b.x=1
a.b.y=2
a.c.x=3
```
*Scheme*
```
a.output=yaml
a.key=name
```
*Output*
```yaml
- name: b
  x: 1
  y: 2
- name: c
  x: 3
```

## Specify entry type
```
[name.]*type=[ignore|string|element|array|multiline]
```
### Ignore
Same as using `!` before namespace in input file.

`namespace.type=ignore` - exclude element from the output;

Example:

*Input*
```
a.b.x=1
a.b.y=2
a.c.x=3
```
*Scheme*
```
a.output=namespace
a.c.x.type=ignore
```
*Output*
```~~~~
b.x=1
b.y=2
```

### String
`namespace.type=string` - force element type to string for JSON and YAML;

Example:

*Input*
```
a.b=1
```
*Scheme*
```
a.output=yaml
a.b.type=string
```
*Output*
```yaml
b: '1'
```

### Element
`namespace.type=element` - in case of XML output, write the entry as an element with inner text, rather than an attribute (which is default).

### Array
`namespace.type=array` - treat all elements in given namespace as array.

Example:

*Input*
```
a.b.b0=1
a.b.b1=2
a.b.b2=3
```
*Scheme*
```
a.output=yaml
a.b.type=array
```
*Output*
```yaml
b:
  - 1
  - 2
  - 3
```
\
Arrays can be defined without explicit definition in the schema if indexes are used:

Example:

*Input*
```
a.b.0=1
a.b.1=2
a.b.2=3
```
*Scheme*
```
a.output=yaml
```
*Output*
```yaml
b:
  - 1
  - 2
  - 3
```

### Multiline
`namespace.type=multiline` - treat all elements in given namespace as multiline string for YAML.

Example:

*Input*
```
a.b.0=row1
a.b.1=row2
a.b.2=row3
```
*Scheme*
```
a.output=yaml
a.b.type=multiline
```
*Output*
```yaml
b: |-
  row1
  row2
  row3
```

## Specify ouptut delimiter
Only for output types: namespace, quotednamespace

Example:

*Input*
```
a.b.c=1
```
*Scheme*
```
a.output=namespace
a.delimiter=_
```
*Output*
```
b_c=1
```

## Disable substitutions
`namespace.substitute=key` - disable substitutions for the value.

Example:

*Input*
```
a.b=1
*.x=*
```
*Scheme*
```
output=namespace
a.x.substitute=key
```
*Output*
```
a.b=1
a.x=*
```
\
`namespace.substitute=none` - disable all substitutions.

Example:

*Input*
```
a.b=1
*.x=*
```
*Scheme*
```
output=namespace
a.x.substitute=none
```
*Output*
```
a.b=1
*.x=*
```

## Specify xml options
```
[name.]*xmloptions=[NoIndent|NewLineOnAttributes]
```

# Installation

From [Nuget](https://www.nuget.org/packages/namespace2xml) as a global tool:
```
dotnet tool install --global namespace2xml --version 0.5.0
```

As a local tool:
```
dotnet tool install namespace2xml --version 0.5.0 --tool-path ./tools
```