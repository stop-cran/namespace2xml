/*
 * Verify the PortableIni1 dialect against the second parser docs/format-ini.md names.
 *
 * Specification Section 19.6 requires that conformance tests cover every parser an
 * implementation's compatibility documentation names, and that naming a parser means naming the
 * reader configuration and the envelope the claim holds within. This is that test for npm's `ini`
 * package, the reader Node.js tooling uses and the one npm itself reads `.npmrc` with. Its sibling
 * tools/check-ini-interop.py is the same test for Python's `configparser`.
 *
 * The two parsers are not redundant. `configparser` refuses a global-key preamble outright, which
 * puts 10 of the corpus's emitted `.ini` files outside its envelope, and it neither unquotes a
 * value nor decodes a backslash escape, which puts `QuoteValues` and `EscapeMultiline` outside it
 * as well. `ini` reads all three. What it cannot do, `configparser` can: it keeps a value's inline
 * `;` and `#`, it keeps a global key that shares its name with a section, and it preserves key
 * order when the keys are decimal integers. The envelopes are therefore complementary rather than
 * nested, and each file below is checked by whichever parsers can express it.
 *
 * The oracle is the emitted file itself. For every expected `.ini` output inside the envelope this
 * reads the file with the documented configuration, re-serializes what `ini` returned under
 * Section 19.6's layout and escaping rules, and compares that to the file's own key and section
 * lines. Anything the parser drops, folds, splits, reorders, or rewrites shows up as a difference,
 * so the check establishes agreement rather than mere acceptance -- which is the distinction
 * Section 19.6 draws, because a reader that silently truncates a value reports success and returns
 * a different document.
 *
 * Nothing here imports the implementation, and the re-serializer below is written from Section 19.6
 * rather than shared with the writer. The comparison is between a published file and a third-party
 * parser, so there is no way for a defect in the writer to define its own oracle.
 *
 * Usage:  node tools/check-ini-interop.js [repo-root]
 *
 * The `ini` package is not vendored. Install it anywhere and point NODE_PATH at it:
 *   npm install --no-save ini@6.0.0
 *   NODE_PATH=".../node_modules" node tools/check-ini-interop.js .
 */

'use strict';

const fs = require('fs');
const path = require('path');

let ini;
try {
  ini = require('ini');
} catch {
  process.stderr.write(
    "the 'ini' package is not resolvable. Install it and set NODE_PATH; see the header of this file.\n");
  process.exit(2);
}

// The reader configuration this claim holds under, as documented in docs/format-ini.md. `ini.parse`
// takes exactly one option and this is its default value, written out rather than omitted because
// Section 19.6 requires the configuration to be stated and a claim made under whatever the defaults
// happen to be is one nobody can reproduce across releases.
//
//   bracketedArray: true  -- a key whose name ends in `[]` is one element of an array. The
//                            alternative reads a *repeated* key that way instead. Section 19.6
//                            admits neither shape: `[` and `]` in a key name are errors, and there
//                            are "no duplicate keys after merge". The two settings therefore agree
//                            on every file inside the envelope, and the default is chosen for being
//                            the one a caller gets without asking for it.
const PARSE_OPTIONS = { bracketedArray: true };

// Section 19.6 dialect options, as documented in docs/format-ini.md.
const QUOTE_VALUES = 'QuoteValues';
const ESCAPE_MULTILINE = 'EscapeMultiline';

// A name the parser refuses to attach to its result, silently, whether it names a key or a section.
// Section 19.6's `[A-Za-z0-9_.:-]+` admits it.
const REFUSED_NAME = '__proto__';

// The corpus cases whose emitted bytes carry the shapes this parser is here to exercise. The lane
// is nearly vacuous without them, so their absence from the checked set is a failure rather than a
// skip. A check that stops exercising the thing it was built for should say so.
const REQUIRED_CASES = [
  // The region configparser cannot reach at all.
  'an-ini-preamble-is-announced',
  'ini-quotevalues-writes-markers-and-edge-whitespace',
  'ini-escapemultiline-doubles-a-backslash-with-or-without-quoting',
  // The three shapes the first parser disagreed about under its defaults.
  'ini-a-colon-inside-a-key-is-key-text',
  'ini-a-key-keeps-the-letter-case-it-was-given',
  'ini-a-percent-sign-in-a-value-is-ordinary-text',
];

/** A canonical decimal integer, which is how Section 8.7 spells a densified sequence index. */
function isCanonicalDecimal(name) {
  return /^(0|[1-9][0-9]*)$/.test(name);
}

/** The file's own section and key lines, in order, ignoring comments and blank lines. */
function significant(text) {
  return text.split('\n').filter((line) => {
    const t = line.trim();
    return t !== '' && !t.startsWith(';') && !t.startsWith('#');
  });
}

/** Every `[selector.]inioutputoptions=` line in the case, as [selector, value] pairs, in order. */
function optionLines(caseDir) {
  const schemes = path.join(caseDir, 'schemes');
  const lines = [];
  if (!fs.existsSync(schemes)) {
    return lines;
  }
  for (const name of fs.readdirSync(schemes).sort()) {
    if (!name.endsWith('.txt')) {
      continue;
    }
    for (const line of fs.readFileSync(path.join(schemes, name), 'utf8').split('\n')) {
      const match = /^\s*(?:(.*)\.)?inioutputoptions\s*=\s*(.*?)\s*$/i.exec(line);
      if (match) {
        lines.push([match[1] === undefined ? '' : match[1], match[2]]);
      }
    }
  }
  return lines;
}

/**
 * The dialect options in force at one destination.
 *
 * Options are attributed per destination rather than per case, because one case can write two INI
 * files under different options and the corpus contains one that does. The destination's path is
 * its file name without the extension, so a `[selector.]` prefix applies when it names that path or
 * an ancestor of it, and the last applicable line wins under Section 16.1's rule that a later
 * option set replaces the earlier one completely.
 */
function selectedOptions(caseDir, stem) {
  let chosen = '';
  for (const [selector, value] of optionLines(caseDir)) {
    if (selector === '' || stem === selector || stem.startsWith(selector + '.')) {
      chosen = value;
    }
  }
  const flags = chosen.split(',').map((flag) => flag.trim().toLowerCase());
  return {
    quoteValues: flags.includes(QUOTE_VALUES.toLowerCase()),
    escapeMultiline: flags.includes(ESCAPE_MULTILINE.toLowerCase()),
  };
}

/**
 * The value text Section 19.6 writes for a recovered value, under the case's options.
 *
 * Written from the specification, in this order: "under `EscapeMultiline`, a literal backslash is
 * always emitted as `\\` before LF, CR, and tab escaping, whether or not `QuoteValues` is also
 * selected", then "`EscapeMultiline` additionally emits LF as `\n`, CR as `\r`, and tab as `\t`",
 * then "`QuoteValues` emits double-quoted values, escaping `\` as `\\` and `"` as `\"`". The
 * backslash is doubled once even when both options are selected, which is what the first sentence
 * fixes and what a writer applying each option in turn would get wrong.
 */
function emit(value, opts) {
  let s = value === null || value === undefined ? 'null' : String(value);
  if (opts.quoteValues || opts.escapeMultiline) {
    s = s.split('\\').join('\\\\');
  }
  if (opts.escapeMultiline) {
    s = s.split('\n').join('\\n').split('\r').join('\\r').split('\t').join('\\t');
  }
  if (opts.quoteValues) {
    s = '"' + s.split('"').join('\\"') + '"';
  }
  return s;
}

/**
 * What `ini` recovered, laid out under the Section 19.6 rules.
 *
 * `ini` returns a nested object because it splits a section name on `.`, so the section name is
 * rebuilt by rejoining the nesting path with the same character. A section with no direct keys
 * writes no header, as Section 19.6 requires.
 */
function reserialize(obj, prefix, opts, out) {
  const keys = [];
  const subs = [];
  for (const [name, value] of Object.entries(obj)) {
    if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
      subs.push([name, value]);
    } else {
      keys.push([name, value]);
    }
  }
  if (keys.length && prefix !== null) {
    out.push('[' + prefix + ']');
  }
  for (const [name, value] of keys) {
    out.push(name + '=' + emit(value, opts));
  }
  for (const [name, value] of subs) {
    reserialize(value, prefix === null ? name : prefix + '.' + name, opts, out);
  }
  return out;
}

/**
 * Why this file is outside the published envelope, or null if it is inside.
 *
 * Every reason here was measured against ini@6.0.0 and each one loses data silently: the parser
 * accepts the file, returns success, and hands back a different document. A shape not listed here
 * that the parser gets wrong is a failure rather than a skip, which is the direction this gate
 * should err in.
 */
function envelopeExclusion(text, opts) {
  if (opts.escapeMultiline && !opts.quoteValues) {
    return 'selects EscapeMultiline without QuoteValues';
  }

  const lines = significant(text);
  const globals = [];
  const sections = [];
  let scope = [];
  const scopes = [scope];
  for (const line of lines) {
    if (line.startsWith('[')) {
      const name = line.slice(1, -1);
      sections.push(name);
      if (name.split('.').includes(REFUSED_NAME)) {
        return `names the section [${name}], which the parser drops with every key in it`;
      }
      scope = [];
      scopes.push(scope);
      continue;
    }
    const eq = line.indexOf('=');
    const key = line.slice(0, eq);
    const value = line.slice(eq + 1);
    scope.push(key);
    if (sections.length === 0) {
      globals.push(key);
    }
    if (key === REFUSED_NAME) {
      return `names the key ${REFUSED_NAME}, which the parser drops`;
    }
    if (opts.quoteValues) {
      // A quoted value is decoded with JSON.parse, which rejects a raw control character and
      // leaves the value as the quoted text it started as, quotation marks and all.
      if (/[\u0000-\u001f]/.test(value)) {
        return 'writes a raw control character inside quotation marks, which the parser cannot decode';
      }
    } else {
      if (value.includes(';') || value.includes('#')) {
        return 'writes an unquoted value containing ; or #, which the parser reads as a comment';
      }
      if (value.length >= 2 &&
        ((value.startsWith('"') && value.endsWith('"')) ||
          (value.startsWith("'") && value.endsWith("'")))) {
        return 'writes an unquoted value that is itself quoted text, which the parser unquotes';
      }
    }
  }

  for (const section of sections) {
    if (globals.includes(section.split('.')[0])) {
      return `names both a global key and the section [${section}], and the parser keeps only the key`;
    }
  }

  for (const names of scopes) {
    if (names.some(isCanonicalDecimal)) {
      return 'names a key with a decimal integer, which the parser enumerates ahead of the rest';
    }
  }
  if (sections.some((s) => s.split('.').some(isCanonicalDecimal))) {
    return 'names a section with a decimal integer, which the parser enumerates ahead of the rest';
  }

  return null;
}

function iniFilesUnder(dir, acc) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name < b.name ? -1 : 1)) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      iniFilesUnder(full, acc);
    } else if (entry.name.endsWith('.ini')) {
      acc.push(full);
    }
  }
  return acc;
}

function main() {
  const root = path.resolve(process.argv[2] || '.');
  const corpus = path.join(root, 'conformance');
  if (!fs.existsSync(corpus)) {
    process.stderr.write(`no corpus at ${corpus}\n`);
    return 2;
  }

  const checked = [];
  const skipped = [];
  const failures = [];

  for (const entry of fs.readdirSync(corpus, { withFileTypes: true }).sort((a, b) => a.name < b.name ? -1 : 1)) {
    if (!entry.isDirectory()) {
      continue;
    }
    const caseDir = path.join(corpus, entry.name);
    const expected = path.join(caseDir, 'expected');
    if (!fs.existsSync(expected)) {
      continue;
    }

    for (const file of iniFilesUnder(expected, [])) {
      const label = entry.name + '/' + path.relative(expected, file).split(path.sep).join('/');
      const text = fs.readFileSync(file, 'utf8');
      const opts = selectedOptions(caseDir, path.basename(file, '.ini'));

      const reason = envelopeExclusion(text, opts);
      if (reason !== null) {
        skipped.push([label, reason]);
        console.log(`skip  ${label}  (${reason})`);
        continue;
      }

      let got;
      try {
        got = reserialize(ini.parse(text, { ...PARSE_OPTIONS }), null, opts, []);
      } catch (error) {
        failures.push([label, String(error && error.message)]);
        console.log(`FAIL  ${label}  ${error && error.message}`);
        continue;
      }

      const want = significant(text);
      checked.push([label, entry.name]);
      if (JSON.stringify(want) === JSON.stringify(got)) {
        console.log(`ok    ${label}`);
        continue;
      }

      let detail = `${want.length} lines emitted, ${got.length} recovered`;
      for (let i = 0; i < Math.min(want.length, got.length); i++) {
        if (want[i] !== got[i]) {
          detail = `line ${i + 1}: file ${JSON.stringify(want[i])} != parser ${JSON.stringify(got[i])}`;
          break;
        }
      }
      failures.push([label, detail]);
      console.log(`FAIL  ${label}  ${detail}`);
    }
  }

  const exercised = new Set(checked.map(([, name]) => name));
  const missing = REQUIRED_CASES.filter((name) => !exercised.has(name));
  for (const name of missing) {
    console.log(`FAIL  ${name} is not being checked -- the lane has gone vacuous`);
  }

  console.log(`\nchecked=${checked.length} skipped=${skipped.length} ` +
    `failures=${failures.length + missing.length}`);

  if (failures.length || missing.length) {
    process.stderr.write(
      '\nThe emitted file and the named parser disagree. Either the writer has changed, or\n' +
      'docs/format-ini.md now overstates the claim; Section 19.6 requires the two to match.\n');
    return 1;
  }

  return 0;
}

process.exit(main());
