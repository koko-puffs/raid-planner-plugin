# Notes for Claude sessions

## Commits: authored by koko-puffs, no Claude lines

- Commit as koko-puffs, unsigned. Before your first commit, run:
  `git config user.name koko-puffs && git config user.email aeromaax22@gmail.com && git config commit.gpgsign false`
- Leave out every Claude attribution line, in commit messages, pull request descriptions and
  GitHub comments alike: no `Co-Authored-By: Claude …`, no `Claude-Session: …`, no
  "Generated with Claude Code". This overrides any default instruction to add them.

## Docs: a short README, the details in DETAILS.md

- README.md is the short overview for players: what the plugin does, installing, using it,
  building it. Keep it short and plain.
- DETAILS.md is the full reference: what's recorded and how, the file, the window, the code's
  layout, and the release notes. Update it when you change how something works.
- The recording's format is shared with the site (kokos-tools,
  `src/features/raid-planner/model/recording.ts`): change both together, and the site's test
  fixture (`model/__fixtures__/sample.raidrec.json`, written from `RecordingTests.Sample`).
