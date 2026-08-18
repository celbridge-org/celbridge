# Writing a report producer

A report is the structured record of one completed operation: generated, read-only, timestamped, and opened as a document. This note is for the host developer adding a producer. It covers the three decisions the format does not make for you — whether the operation deserves a report at all, which findings to declare, and what to put in each occurrence.

The two producers to read as examples are `ProjectLoadReporter` (accumulates across a whole load, always writes) and `ResourceOperationNotifier` (writes only when a batch failed on more than one thing).

## Does this operation deserve a report?

Write one when there is per-item detail worth reading beyond the summary line — more than one item, or one item whose reason cannot be stated in a notification line. Otherwise let the notification stand alone with no action.

A single rename failure is fully expressed by "Could not rename 'notes.txt': the file is locked". Generating a one-row document for it is noise, and a `logs:reports/` folder churning with trivia devalues the reports that matter.

The project load is the exception that proves the rule: it writes on **every** load, including a clean one, because the health indicator needs a resting state and something to open. A report that leads with facts about the subject is worth opening whether or not it found anything.

## Where the report goes

- `logs:reports/` for a host-generated report about the user's project. Use `ReportLocation.WriteReportAsync`, which resolves the folder and returns the resource key to open.
- `project:` for a report the user asked for and will keep, share, or commit. An ordinary resource at the name the caller asked for, with no rotation.

Under `logs:reports/` the current report of a kind sits at `{id}.report` and the previous one moves into `history/`, pruned to the most recent 5 per id. Pick an `id` per *kind of operation*, not per run: a copy report must not displace a delete report's history. `logs:` is unwatched, so a producer that reopens its own report passes `ForceReload` on the open command rather than waiting for a change event.

A producer that flushes several times during one operation is revising one report, not writing several. Stamp `GeneratedAt` with the start of the operation so every flush addresses the same file and history gains one entry per operation.

## Declaring a finding

A finding kind is declared once in `ReportFindingCatalog`, and the producer emits occurrences of it through `ReportFinding.Create`. The descriptor carries what is true of every occurrence:

- **Code** — `CEL_<AREA>_<NNN>`, matching `CEL_FS_001` on `DirectFileSystemAccessAnalyzer`. Add to an existing area group, or open a new one when the area is genuinely new.
- **Message template** — one string per finding kind, formatted with per-occurrence arguments. This is the unit that gets localized, rather than a string per emit site.
- **Default severity** — never `Info`. `Info` is what a fact carries; a finding is something that needs attention.

**Codes are never reused or renumbered.** A retired finding's code stays retired. Reports persist on disk, so a recycled code silently changes what an old report said.

**Keep the set small.** Every code is a commitment that it can be looked up, and a code that cannot is noise. Mint one when the finding has a distinct cause and a distinct remedy — "could not be copied" and "could not be deleted" fail for different reasons and are worth separating; every distinguishable circumstance within one of those is not.

`ReportCodeCoverageTests` holds the rest: codes are unique, they follow the one shape, and nothing is declared that no producer emits. A descriptor added without an emit site fails the build.

## Filling in an occurrence

`Resource` is what the finding is about. `Target` is a second resource the first relates to, such as the missing target of a broken reference.

`Detail` is **per-occurrence, and only per-occurrence**: the parse error, the rejected value, the exception. Anything true of every occurrence belongs to the code — the message states it, and the help topic will explain it. A constant `detail` repeated on every row says nothing the message did not, and once findings group it becomes the same paragraph printed a hundred times.

That rule also stops a report asserting more than it knows. The reference scan is lexical, so it cannot tell a live reference from a key quoted as an example — but that is a property of the check, true of every result. Stated on an individual finding it reads as a claim about *that* reference, which the producer has no basis for.

Give an item an `OpenResource` action naming its own resource wherever the reader would want to jump to it; the editor turns the resource cell itself into the link. Add a `Location` when the finding has a genuine position. `OpenResource` is the only action kind, deliberately: a report can be written into `project:` and arrive from outside, so it must not be able to name arbitrary work to run nor any destination outside the project.

## Sections

A section declares its `Kind`. `Facts` sections hold readings about the subject — counts, versions, durations — as a label in `Message` and the reading in `Value`, and their items carry no code. `Findings` sections hold diagnostics, and their items do.

Findings group in the editor by code and message, and a group of more than one renders as a table whose columns are decided by what its rows actually carry. Two consequences for a producer: fill the same fields across the occurrences of one code, and do not repeat a count that the report's summary line already states.

## Text

Every string a reader sees in a report is resolved by the producer and written into the file as text, never as a localization key. A report has to be readable in isolation — it is a JSON file that can be shared, committed, or read outside Celbridge — and once contributions can write into one, deferred resolution would leave dangling keys in a report opened after the contribution was removed.

The code is what keeps that safe: it is the locale-invariant identity while the message is presentation, exactly as `CS0104` is stable while its message is not.
