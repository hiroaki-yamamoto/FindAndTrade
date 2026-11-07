---
name: doc
description: |
  Documentation Skill Rules.
  You must follow this rule when writing documentation in addition to general rules. For General Rule, refer to [/.agents/skills/general/SKILL.md](/.agents/skills/general/SKILL.md).
---

## Documentation Rules

The following documentation rule is applied to any comments about code, including
general code comments, comments for documentation of code, Pull Request title
and description.

- Output the generated documentation in English.
- The documentation should be written in markdown format. However, if
  the documentation is for a module, function, struct, or enum, it should be
  written in doc-comment that language(s) we use support.
- Provide a code example that utilizes the documentation testing feature (doctest),
  assuming the language environment supports automatic verification of code
  snippets within the documentation.
- The documentation should be well-documented, clear, concise, and easy to
  understand. It should explain the purpose of the item being documented,
  how to use it, and any important details or caveats that users should be
  aware of.
- If the item being documented is a function, the documentation should include
  a description of the function's parameters, return value, and any errors that
  it may produce.
- If the item being documented is a struct or enum, the documentation should
  include a description of the struct or enum, its fields or variants, and any
  important details or caveats that users should be aware of.
- If the item being documented is a module, the documentation should include a
  description of the module, its purpose, and any important details or caveats
  that users should be aware of. It should also include a brief overview of the
  items contained in the module.
