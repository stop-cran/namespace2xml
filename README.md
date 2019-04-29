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

Reference one value from another:

Input:
```
a.x=1,${b.y},3
b.y=2
```
Evaluates to:
```
a.x=1,2,3
b.y=2
```

Reference with wildcards:
Input:
```
a.x=1
a.*.y=2
```
Evaluates to:
```
a.x=1
a.x.y=2
```

Input:
```
a.x=1
a.*.y=*
```
Evaluates to:
```
a.x=1
a.x.y=x
```

Input:
```
a.x=1
b.*.=${a.*}
```
Evaluates to:
```
a.x=1
b.x=1
```

All use-cases above can be combined and used simultaneously, e.g.:

Input
```
a.x.y=1
a.*.*=1,${b.*},${c.*}
b.x=2
c.y=3
```
Evaluates to:
```
a.x.y=1,2,3
```

Input
```
a.*=1,${b.*},${c.*}
b.x=2
c.x=3
```
Evaluates to:
```
a.x=1,2,3
```

Input
```
a.x.y=1
a.*.*=1,*,${c.*}
c.y=3
```
Evaluates to:
```
a.x.y=1,x,3
```


# Scheme description


## Supported output types
```
[name.]*output=[xml|json|yaml|ini|namespace]
```

## Set output file name or path
```
[name.]*filename=<overridden file name>
```

## Change name of the root element(s)/namespace/INI section name
```
[name.]*root=<name, maybe hierarchical, separated by dots>
```
For all direct child elements treat child entry name as a key and group grand-children by that key:
```
[name.]*root=<a name of XML attribute or YAML/JSON property name used to write the value of the key>
```

Example:

*scheme.txt:*
```
a.output=yaml
a.key=b
```

*input.txt:*
```
a.key1.x=1
a.key1.y=2

a.key2.x=5
a.key2.y=7
```

Call:
```
namespace2xml -i input.txt -s scheme.txt
```

*output (a.yml):*
```
key1:
  x: 1
  y: 2
key2:
  x: 5
  y: 7
```


## Specify entry type

`hiddenKey` - same as `key`, but the name of the key does not go to the output;

`ignore` - exclude from the output;

`csv` - split the value by comma and produce multiple XML/JSON/YAML elements;

`string` - force element type to string for JSON and YAML;

`element` - in case of XML output, write the entry as an element with inner text, rather than an attribute (which is default).

It's possible to specify multiple types, separated by comma, e.g. `csv,element` means split the value by comma and produce XML element with inner text fora each part.



# Installation

From [Nuget](https://www.nuget.org/packages/namespace2xml) as a global tool:
```
dotnet tool install --global namespace2xml --version 0.5.0
```

As a local tool:
```
dotnet tool install namespace2xml --version 0.5.0 --tool-path ./tools
```