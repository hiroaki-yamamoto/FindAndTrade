---
name: git-doc
description: |
  Git Commit Message & Github Pull Request Rule.
  You must follow this rule when generating git commit message and pull request in addition to general documentation rules.

  For General Documentation Rule, refer to
  [/.agents/skills/doc/SKILL.md](/.agents/skills/doc/SKILL.md).
---

# Git Commit Message / Pull Request Title Rules

When generating a git commit message and pull request title, you MUST adhere to the [Conventional Commits](https://www.conventionalcommits.org) specification with the addition of Gitmoji.

## Format

```
<type>[optional scope]: <gitmoji> <description>

[optional body]

[optional footer(s)]
```

## Rules

1. **Title Follows Conventional Commits with Gitmoji**: The commit title and pull request title must begin with a type, followed by an optional scope, a colon, a space, a relevant gitmoji, and then a description.
   - Example (Feature): `feat: ✨ Add interpretation of graphql`
   - Example (Documentation): `doc: 📝 Updated README.md`
2. **Prioritize Code over Docs**: If a commit contains both code and documentation changes, the code changes take precedence for the commit type. Documentation changes are NOT considered feature-related changes.
3. **Follow Code Documentation Rules**: The title and body of the commit message must also follow the `Code Documentation Rules` whenever possible (e.g., using complete sentences in English, clear and concise descriptions).
4. **Common Types**:
   - `feat`: A new feature
   - `fix`: A bug fix
   - `doc`: Documentation only changes
   - `style`: Changes that do not affect the meaning of the code (white-space, formatting, missing semi-colons, etc)
   - `refactor`: A code change that neither fixes a bug nor adds a feature
   - `perf`: A code change that improves performance
   - `test`: Adding missing tests or correcting existing tests
   - `build`: Changes that affect the build system or external dependencies
   - `ci`: Changes to CI configuration files and scripts
   - `chore`: Other changes that don't modify src or test files
   - `revert`: Reverts a previous commit
