.. _ansible_collections.stop_cran.namespace2xml.docsite.what_is_in_this_collection:

What is in this collection
==========================

The public interface is two plugins, and nothing else in this collection is one.

- :ansplugin:`stop_cran.namespace2xml.render#filter` renders data held in **play variables** on
  the controller and returns text.
- :ansplugin:`stop_cran.namespace2xml.render#module` renders a managed node's **own files** in
  place, and reports whether anything changed.

Both drive the same transformer, `namespace2xml
<https://www.nuget.org/packages/namespace2xml>`_, a .NET tool that reads namespace text or an
existing document and writes XML, JSON, YAML, INI or namespace text. The plugins do not
reimplement it; they marshal inputs, run it, and interpret the result.

Which one to reach for, the arguments each takes, the fidelity limits, and how to report a
problem are all in the `collection README
<https://github.com/stop-cran/namespace2xml/blob/master/ansible/README.md>`_. Per-argument
reference, including the specification section each argument corresponds to, is on the two
plugin pages above, or offline:

.. code-block:: bash

   ansible-doc -t filter stop_cran.namespace2xml.render
   ansible-doc -t module stop_cran.namespace2xml.render

Both plugins are called ``render``, so the ``-t`` is not optional.

The empty pages under ``module_utils``
--------------------------------------

Galaxy lists everything a collection ships, so ``plugins/module_utils/n2x.py`` and
``plugins/module_utils/render_node.py`` appear in the contents with empty documentation. That is
not an omission that can be corrected: ``module_utils`` is not one of Ansible's documentable
plugin types, so there is no documentation block to write for one and no renderer that would
read it.

They are internal helpers — argument marshalling, value escaping, tool resolution, byte
comparison — shared by the two plugins. **They carry no stability promise**, are not importable
as a supported interface, and may change in any release. Nothing you write should depend on
them. Their behaviour is covered by the collection's unit tests, and their source is in the
`repository <https://github.com/stop-cran/namespace2xml/tree/master/ansible/plugins/module_utils>`_.

Where the rules come from
-------------------------

The transformer's behaviour is defined by a specification rather than by its implementation, and
the plugins' error messages cite it by section number. When a message says "section 21.1", that
is a pointer you can follow.

- `Specification summary
  <https://github.com/stop-cran/namespace2xml/blob/master/ansible/docs/specification-summary.md>`_
  — the short version, aimed at someone using the plugins.
- `Full specification
  <https://github.com/stop-cran/namespace2xml/blob/master/docs/specification.md>`_ — normative,
  and the thing to quote in a bug report.

Reporting a problem
-------------------

`Open an issue <https://github.com/stop-cran/namespace2xml/issues/new/choose>`_ and set the
**Component** field to ``Ansible collection``: if a playbook was in the loop the fix lands here,
even when the message came from the tool underneath. What makes a report actionable — the inputs,
the scheme, what you expected and which section says so — is set out under "Found a problem?" in
the README, along with what to include from ``ansible-playbook -vvv``.
