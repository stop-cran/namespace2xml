# Mixedness is a property of the merged element

Section 11.4 evaluates mixedness "at concrete merge time across all input contributions to that
element", and requires that "if the merged element is mixed, every content node uses its `#n`
wrapper even when it originated in an element-only source document".

Two documents contribute to `a`. The first writes it as mixed content; the second writes it as an
element-only element, which on its own would address its child as `a.b`. The merged element is
mixed, so that child is addressed as a content node instead, and one element is no longer reachable
at two addresses.

The converted content is allocated above the content tokens the mixed document already occupies,
because Section 17.4 states that "child elements in mixed content do not deep-merge with elements
from another contribution". Reusing the ordinals the element-only document assigned for sibling
ordering alone would have landed its child on top of the other document`s first text node.

Legacy 2.4.0 had no XML input at all, so there is nothing to compare against.
