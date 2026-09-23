# Downloads

A file downloaded from any page the application hosts lands in the project's downloads folder by one
path, whichever surface the link was on, and the badge in the title bar lists what the session has
downloaded. Read the [README](README.md) for the invariants, evidence rules and levels.

## Surfaces

The pages a download starts from: an HTML document, a `.webview` document and a package utility. The
download badge, its count, and the list it opens: each row, a running row's cancel button, a finished row's
remove button, and Clear All.

## Cases

| Situation | Action | Expected | Level |
|---|---|---|---|
| An HTML document with a `download` link to another project file, and nothing downloaded yet this session | click the link | the badge, absent until now, appears, and the file lands in `downloads/` identical to the one linked | 1 |
| A `.webview` document on a page with a `target="_blank"` link to a response marked as an attachment, which the server holds back | click the link, then read the address bar before the response arrives and again once the file has landed | the file lands in `downloads/`, no system browser opens, and the document still shows the page the link was on, with the address bar naming it both times | 1 |
| A package utility whose page offers a file through a `download` link | click the link | the file lands in `downloads/`, and nothing new appears in the operating system's Downloads folder | 2 |
| A `.webview` document on a page with a plain link to a response marked as an attachment, which the server holds back | click the link, then read the address bar before the response arrives and again once the file has landed | the file lands in `downloads/`, the page stays on screen with the address bar naming it both times, and the log records no navigation failure | 2 |
| One completed download | click its row in the list | the list closes and the Explorer shows the file selected | 2 |
| A file already downloaded once | download it again, slowly enough to see it running | a second row and a second file named `<name> (1).<ext>`, which its row shows while it is still running | 2 |
| A download still running, the list open | click its cancel button | the list stays open, the row says the transfer was canceled and gives no size, neither the row nor the badge shows it as a failure, the badge's count no longer includes it, and nothing is left in `downloads/` or the staging folder | 2 |
| Several finished downloads and one still running | click Clear All | the finished rows go and the running row stays, and their files stay in `downloads/`; once the last download lands, a second Clear All empties the list and the badge goes | 2 |
| Finished downloads, one of which failed, the list open | click the failed row's remove button, then each remaining row's | the failed row leaves the list and the others stay, the count drops and the badge loses the error color, and removing the last row closes the list and the badge goes, with every downloaded file still in `downloads/` | 2 |
| A download running when the server drops the connection | let it fail, then wait for the badge to settle | one row, however often the platform retries first, says the transfer did not complete, the badge stops spinning, and nothing is left in `downloads/` or the staging folder | 2 |
| A download of several hundred megabytes, made after an undoable change in the Explorer such as a new folder | download it, then undo in the Explorer | the application answers input throughout, the file carries the platform's mark of the web, and undo reverts the earlier change and leaves the file where it landed | 2 |
| A `.webview` document on a page with a `download` link | click the link | the file lands in `downloads/`, as it does from an HTML document | 3 |
| An HTML document offering a file the page builds itself, and a `download` link that asks for a new window | click each | both land in `downloads/`, and no browser opens | 3 |
| An HTML document with a plain link to a response from another server marked as an attachment | click the link | the document asks before handing the address to the system browser, and nothing lands in `downloads/` | 3 |
| A completed download | delete its file in the Explorer | its row leaves the list and the count drops, and the badge goes with the last row | 3 |
| Two downloads of the same file started together | start both | two files under distinct names, neither overwritten | 3 |
| A download running from a `.webview` document | close the document's tab | the download stops, its row says the transfer stopped with the document, and nothing is left in `downloads/` or the staging folder | 3 |
| A download running, and the downloads folder changed in Project Settings while it runs | let it land | the file lands in the folder the download reserved when it started, and the next download goes to the new folder | 3 |
| The list open with the keyboard on a running row's cancel button | let that download finish | the list stays open, and the keyboard stays on that row, which now finds the file | 3 |
| The list open with the keyboard on a running row's cancel button | press Space, then Space again | the first press leaves the row saying the transfer was canceled with the keyboard on its remove button, and the second takes the row off the list | 3 |
| A downloads folder Celbridge reserves, such as `.git`, typed into Project Settings | download a file | the field says the folder is reserved and the project file gains no key, and the file lands in `downloads/` | 3 |
| A downloads folder set in Project Settings to a folder the project does not have yet | reload the project, then download a file | the badge is absent after the reload while earlier downloads' files remain, and the new file lands in the named folder, which the download creates | 3 |
| A page with a link and an image | use the context menu's download or save items on each | on Windows, Save As writes where its picker names, adding a row only when that is the downloads folder its picker opened on; on macOS, Download Linked File and Download Image land in `downloads/` | 3 |
| A PDF and a markdown document | open the PDF in the file viewer and the markdown document with its preview | each displays, and nothing is downloaded | 3 |

Most cases need one page offering a download in each shape the table names: a `download` link to a file
beside it, a file the page builds itself, a `download` link that asks for a new window, and plain and
`target="_blank"` links to a response marked as an attachment. Keep the page and its file in a folder of
their own, so a landed copy is never mistaken for the original, and give the file content that can be
compared byte for byte.

An HTML document is served by the application itself, so its links are real downloads over HTTP with
nothing leaving the machine. The application's server marks nothing as an attachment, though, and a
`.webview` takes an http or https address only, so serve the same folder from a small server of your own on
a loopback port and point the `.webview` there. That server supplies what the application's cannot: a
response marked as an attachment, one it holds back for about ten seconds before sending its headers, one
slow enough to act on while it runs, one that drops the connection part way through, and one of several
hundred megabytes. Holding the headers back keeps the link's navigation in flight, since nothing can tell
it is a download until they arrive, and that is what gives the address bar time to be read before the
download starts. Give the attachment a type the page could display, such as plain text, so that only the
marking makes it a download. Start the two downloads that run together with two clicks a moment apart on
two links leading to one file name: a download the page starts by itself is blocked on Windows, where
Chromium allows a page only one download without a user gesture, and on macOS WebKit cancels all but the
last of several a script starts at once, so neither platform runs both unless a person clicks twice.

The utility is a package in the project's `packages/` folder whose page offers the file (the agent guide
`utility_documents` describes the manifest). Packages are found when the project loads, so add it before
launching or reload the project after.

Read outcomes from disk rather than from the list: what landed in the downloads folder and its bytes, the
operating system's Downloads folder, and the staging folder, `.celbridge/temp/downloads/`, which is empty
whenever nothing is running. The list is the application's own chrome, so find rows with a screenshot and
prove what they did with the files.

## By hand

The badge's animations, and two checks that rest on real pointer input where synthetic input gives false
results: the title bar, and the application's own list drawn over a hosted page.

- With the badge absent, download one file: the badge should flash **once** as the download lands.
- Click two download links in quick succession: **one** flash for the pair, not one per file.
- Cancel a running download: **no** flash, since nothing arrived.
- Clear All with only finished downloads listed: the list should close first and the badge go after it,
  rather than the badge vanishing out from under an open list.
- Packaged Windows head: click the badge while it shows the progress ring, with a count of one, and with a
  count of two digits, then Clear All and drag the window by the spot the badge occupied. A badge the
  window's drag region was not carved out for looks like a dead button rather than an error.
- macOS: with the list open over a document showing a web page, click a row, a cancel button and Clear All,
  and check the log after each for a web surface reporting focus. The page must not take the click.

## Not covered

Files the application fetches for itself, such as a Workshop package install, which never reach the
downloads list. What a row says beyond its name and outcome: its progress, sizes and time estimates.
Choosing the downloads folder with its picker, which the Workspace plan covers. Where an HTML document's
links lead, which the Web Documents plan covers.

## Platform

The context-menu case differs by design. WebView2's menu offers Save As, whose picker opens on the
project's downloads folder. A file saved to another folder goes there and reaches no row, since the user
named somewhere outside the project; one saved to the folder the picker opened on is a download like any
other and gets a row. Check both in the case. WebKit's menu has no Save As, and its Download Linked File
and Download Image are downloads like any other.

A download does not outlive the document it was started from on Windows, because the platform reports its
progress and its outcome through that document's web view. On macOS the downloads are routed by one
process-wide listener instead, so a transfer there may well carry on after its tab closes; a macOS run
should record which it saw.

The mark of the web is the `com.apple.quarantine` attribute on macOS, which WebKit gives every download, and
the `Zone.Identifier` stream on Windows, which WebView2 writes according to where the file came from. On
Windows, run the large download against a real site, such as the `.msix` on the Celbridge download page,
which is the file that case was written for: Microsoft Defender scans it for tens of seconds after it lands,
and the file's arrival waits on that scan, so its row can take a few seconds to settle while the
application must still answer input.

On Windows, keep a file of the same name in the operating system's Downloads folder for the repeat-download
case, since that is what made WebView2 suggest a numbered name of its own.

WebView2 has been seen to retry a download whose connection dropped several times before it gives up,
announcing each retry as a new download, so the dropped-connection case's row can run for a few seconds
before it fails; it has also been seen to give up on the first drop. Either is a pass. The defect that case
is there to catch is a row per retry, or rows left running for ever with the badge spinning, so the absence
of retries is not itself a finding. WebKit has not been seen to retry either, and a macOS run should record
which it saw.

The prompt an HTML document shows for a link to another server stops the page following the link on both
heads, and neither sends a request for the destination it refuses, so a link to an attachment that is
refused downloads nothing and reaches nobody. The Web Documents plan is where that request is checked.
