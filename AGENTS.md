<!--
Copyright (C) 2026 HyPrism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Agent Documentation Policy

This file is intended for automation agents (bots, CI scripts, or assistant agents) that interact with the HyPrism repository. It explains how an agent should use the project's documentation and the responsibilities the agent must follow when making changes

## Read the docs to learn more 💡

- Agents MAY read any files under `Docs/` to learn about features, architecture, build processes, APIs, and packaging
- Prefer `Docs/` Markdown pages (see `Docs/English/` and `Docs/Russian/`) for platform-specific notes

## Documentation responsibilities (required) ✅

An agent MUST:

1. **Update documentation for every change** that affects behavior, features, APIs, configuration, packaging, or developer workflows
2. **Update user-facing docs** when UI or feature behavior changes
3. **Update developer docs** when build steps, CI, packaging, or contributor workflows change
4. **Update API / bridge docs** when adding, renaming, or removing backend methods accessed by the frontend

## How to make a docs change 🔧

- Create a branch named `docs/update-<short-description>` or include the documentation changes in the same feature branch as the code change
- Add or modify files under `Docs/` and keep content clear and concise
- Validate changes locally: run `make html` in `Docs/` or `sphinx-build` to ensure no build errors
- Use commit messages starting with `docs:` (for example: `docs: add patcher instructions for ClientPatcher`)
- Open a Pull Request including a short PR description and a checklist of the docs updated

## Docs & PR checklist 📋 🤖

Before merging changes, ensure the following:

- [ ] Documentation updated (user / developer / API) for the change
- [ ] Docs build passes locally or in CI (run `make html` in `Docs/` or verify Read the Docs build)
- [ ] Spell-check or lint docs and code
- [ ] README updated if needed

## When unsure

If you're uncertain which docs to update, open a draft PR and request a docs review from a maintainer

---

*This policy is a lightweight, actionable guide so agents can keep the docs accurate and up-to-date*