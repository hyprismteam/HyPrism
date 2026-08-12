<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Agent documentation policy

This file is intended for automation agents (bots, CI scripts, or assistant agents) that interact with the HyPrism repository. It explains how an agent should use the project's documentation and the responsibilities the agent must follow when making changes

## Read the docs to learn more 💡

- Agents may read any files under `Docs/content/` to learn about features, architecture, build processes, APIs, and packaging
- English pages live under `Docs/content/en/` and Russian pages live under `Docs/content/ru/`
- Keep both language trees aligned when changing shared behavior

## Documentation responsibilities (required) ✅

An agent MUST:

1. **Update documentation for every change** that affects behavior, features, APIs, configuration, packaging, or developer workflows
2. **Update user-facing docs** when UI or feature behavior changes
3. **Update developer docs** when build steps, CI, packaging, or contributor workflows change
4. **Update API / bridge docs** when adding, renaming, or removing backend methods accessed by the frontend

## How to make a docs change 🔧

- Add or modify MDX pages under `Docs/content/` and keep the writing clear and concise
- Do not use em dashes in documentation or code comments
- Do not end the final sentence of a section or list item with a period
- Run `pnpm install` in `Docs/` when dependencies change
- Validate content and types with `pnpm check` in `Docs/`
- Validate the Docusaurus static export with `PAGES_BASE_PATH=/HyPrism pnpm build` in `Docs/`
- Use commit messages starting with `docs:` when a commit contains only documentation changes
- Include a short documentation checklist in the pull request description

## Docs & PR checklist 📋 🤖

Before merging changes, ensure the following:

- [ ] Documentation updated (user / developer / API) for the change
- [ ] `pnpm check` passes in `Docs/`
- [ ] The Docusaurus static export builds for the `/HyPrism` base path
- [ ] Spell-check or lint docs and code
- [ ] README updated if needed

## When unsure

If you're uncertain which docs to update, open a draft PR and request a docs review from a maintainer

---

*This policy is a lightweight, actionable guide so agents can keep the docs accurate and up-to-date*
