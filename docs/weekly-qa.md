# Weekly functional QA

`Functional QA` runs on protected `main` each Monday at 04:17 UTC. Its
deterministic job builds Zipper and runs the existing five-flow basic CLI E2E
smoke, retaining a log for 14 days. This job does not need Factory credits.
If smoke fails, the scheduled Droid job is skipped and the weekly issue
points to the failed log.

The optional Droid job compares `main` to its most recent ancestor older than
seven days. It runs only when the changed paths match the configured CLI app
patterns and `FACTORY_API_KEY` is available in the `QA` environment. The
model step is capped at 20 minutes and is asked for two change-specific cases
not covered by basic smoke. Missing credentials, expired access, exhausted
credits, or a failed tool/model call do not skip the deterministic job.
Inspect the Droid job outcome and artifact; a failed Droid call is **not** a
passing functional test. The GitHub job can still fail if its configuration
or an actual report validation fails.

Each scheduled run opens one `testing`/`P3` issue titled by the run's creation
ISO week, or comments on the same week's issue if retried. It links to the run,
records both job outcomes, and asks for a concrete CLI coverage improvement. A clean
run is an improvement *review*, not a reported Zipper bug. Verify any
failure in the linked logs before filing a defect. Issue creation needs a
working `issues: write` GitHub token; repository policy may deny it.

Manual dispatch is unchanged: supply a full ancestor `base_sha` from
protected `main`. It does not create a weekly issue or run the deterministic
weekly job. The separate pull-request reporter is unchanged.
