# Security Policy

## Supported Versions

LibreKO has no numbered releases. The head of the `main` branch is the only supported version: a
security fix lands there, and a server operator picks it up by pulling and redeploying. Older
commits, forks and third-party servers built from them receive no fixes.

| Version | Supported          |
| ------- | ------------------ |
| `main`  | :white_check_mark: |
| anything else | :x:          |

## Reporting a Vulnerability

Report a vulnerability privately, not as a public issue: a public report tells every server
operator's players how to abuse it before a fix exists.

Use one of these:

- **Report a vulnerability** under the repository's Security tab, which opens a private advisory
  that only the maintainer can read.
- E-mail `zeusafk@gmail.com` with "LibreKO security" in the subject.

Say what the problem lets an attacker do, how to reproduce it (the packet, the command, the client
action, the sequence of steps), and which commit you tested against. A proof of concept helps; an
exploit run against a server you do not operate does not, and is itself a violation of the code of
conduct.

What to expect:

- An acknowledgement within seven days.
- A fix on `main` as soon as one exists, with the advisory published after it lands so that
  operators can update before the details are public. You are asked to keep the report private until
  then.
- Credit in the advisory and in `CONTRIBUTORS.md`, unless you ask not to be named.
- If the report is declined, an explanation of why: the behavior is intended, it is not exploitable,
  or it is the responsibility of a third-party server operator's own setup.

In scope: the login server, the game server, the shared library, the quest compiler and the client
in this repository. Out of scope: servers run by others and their configuration, the original game,
and anything that only affects a player's own machine through their own actions.
