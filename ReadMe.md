<!-- title: SubTubular --> <!-- of the printed HTML see https://github.com/yzhang-gh/vscode-markdown#print-markdown-to-html -->
# SubTubular <!-- omit in toc -->

A **full-text search** for **[YouTube](https://www.youtube.com/)** searching **subtitles** and **video metadata** and returning text results including **time-stamped video links** - allowing you to find and go directly to the relevant content in videos, playlists, even entire channels.
Comes with both a **graphical** (GUI, `SubTubular.Gui.exe`) and **command line interface** (CLI, *Shell* , `SubTubular.Shell.exe`) wrapping the same library (`SubTubular.dll`).

<img src="./SubTubular.ico" align="right"
    title="Not just a propelled sweet potato with trench binos&#13; - but the best-looking tuber in the soup." />

- [Overview](#overview)
- [Shell Commands](#shell-commands)
  - [Common `search` and `keywords` command options](#common-search-and-keywords-command-options)
  - [`search`-only command options](#search-only-command-options)
  - [Common `playlist` and `channel` scope options](#common-playlist-and-channel-scope-options)
  - [open, o](#open-o)
  - [clear-cache, clear](#clear-cache-clear)
    - [Arguments](#arguments)
    - [Options](#options)
  - [release, r](#release-r)
    - [Commands](#commands)
    - [Arguments](#arguments-1)
- [Examples \& use cases](#examples--use-cases)
  - [Find specific parts of podcasts or other long-running videos](#find-specific-parts-of-podcasts-or-other-long-running-videos)
  - [Search a playlist for mentions of a certain topic](#search-a-playlist-for-mentions-of-a-certain-topic)
  - [Using wild cards and exact matching in multi-word phrases](#using-wild-cards-and-exact-matching-in-multi-word-phrases)
  - [Search a channel for specific content](#search-a-channel-for-specific-content)
  - [Exploring a channel or playlist via its keywords](#exploring-a-channel-or-playlist-via-its-keywords)
  - [Find material for a supercut of a phrase](#find-material-for-a-supercut-of-a-phrase)
- [Tips \& best practices](#tips--best-practices)
  - [Writing queries](#writing-queries)
  - [Searching auto-generated subtitles](#searching-auto-generated-subtitles)
- [Fair use](#fair-use)


# Overview

## Searches <!-- omit in toc -->
- video **title**, **description**, **keywords** and **captions** (a.k.a. *subtitles*, *closed captions*/*CC* or *transcript*)
- **across multiple captions** and description lines
- in the scope of one or **multiple videos**, **playlists** and **channels**
- while **ignoring the case (and accent)** of the search terms

## supporting <!-- omit in toc -->
- the full feature set of the [LIFTI query syntax](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/) including
- [exact](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/#exact-word-matches), [fuzzy](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/#fuzzy-match-) and [wild card](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/#wildcard-matching) matching
- **multi-word phrases** with words in [exact](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/#sequential-text-) or [loose](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/#following-) sequence or configurable [nearness](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/#near--and-n) to each other
- **multiple search terms**, phrases or complex queries combinable with [and](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/#and-) and [or](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/#or-) in nested [bracketed expressions](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/#bracketing-expressions)
- [field-specific queries](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/#field-restrictions-field) for **title**, **description**, **keywords** and language-specific **captions**

## returning <!-- omit in toc -->
- a list of search results with **highlighted** matches
- including **time-stamped video links** to the corresponding part of the video for caption matches
- as a text or HTML file if you need it

## caching <!-- omit in toc -->
- searchable **video metadata** and **subtitles** in **all available languages**
- **videos in playlists** or channels **for a configurable time**
- **channel aliases** like handles, slugs or user names
- **full-text indexes** for all searched texts
- so that **subsequent searches** on the same scope can be done **offline** and are way **faster** than the first one
- in your **local user profile**, i.e.
  - `%AppData%\Roaming` on Windows
  - `~/.config` on Linux and macOS
- until you **explicitly clear** them

## requiring <!-- omit in toc -->
- **no installation** except for the [**.NET 9 runtime**](https://dotnet.microsoft.com/en-us/download/dotnet) (which you may have installed already)
- **no YouTube login**

## thanks to <!-- omit in toc -->
- [**YoutubeExplode**](https://github.com/Tyrrrz/YoutubeExplode) licensed under [MIT](https://github.com/Tyrrrz/YoutubeExplode/blob/master/License.txt) for doing a better job at **getting the relevant data off YouTube**'s public web API than YouTube's own [Data API v3](https://developers.google.com/youtube/v3/) is able to do at the time of writing. And for not requiring a clunky app registration and user authorization for every bit of data on top of that. A real game-changer!
- [**LIFTI**](https://github.com/mikegoatly/lifti) licensed under [MIT](https://github.com/mikegoatly/lifti/blob/master/LICENSE) for the heavy-lifting on the **full-text search** with indexing, fuzzy and wild card matching among other powerful query features. And for making them accessible through a well-designed API with awesome documentation.
- [**AngleSharp**](https://github.com/AngleSharp/AngleSharp) licensed under [MIT](https://github.com/AngleSharp/AngleSharp/blob/master/LICENSE) for making **HTML output generation** easy and intuitive
- [**Avalonia**](https://github.com/AvaloniaUI/Avalonia) licensed under [MIT](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md) for the multi-platform framework the GUI is built on
- [**Fabulous for Avalonia**](https://github.com/fabulous-dev/Fabulous.Avalonia) licensed under [Apache 2.0](https://github.com/fabulous-dev/Fabulous.Avalonia/blob/main/LICENSE.md) for wrapping Avalonia into a MVU framework for F#
- [**Octokit**](https://github.com/octokit/octokit.net) licensed under [MIT](https://github.com/octokit/octokit.net/blob/main/LICENSE.txt) for wrapping the Github API and offering easy access to releases and their assets enabling the **download of and showing release notes for different releases**

## **not** providing <!-- omit in toc -->
- subtitle download in any common, reusable format (although that would be an easy addition if required).


# Shell Commands

The CLI uses the [System.Commandline syntax](https://learn.microsoft.com/en-us/dotnet/standard/commandline/syntax) and has the following commands:

| shorthand, name \<arguments>                                            |                                                                                         |
| :---------------------------------------------------------------------- | :-------------------------------------------------------------------------------------- |
| `s`, `search`                                                           | Search the subtitles and metadata of videos in the given scopes.                        |
| `k`, `keywords`                                                         | List the keywords of videos in the given scopes.                                        |
| `clear`, `clear-cache` `<all\|channels\|playlists\|videos>` `<aliases>` | Deletes cached metadata and full-text indexes for `channels`, `playlists` and `videos`. |
| `rls`, `release`                                                        | List, browse and install other SubTubular releases.                                     |
| `o`, `open` `<app\|cache\|errors\|output\|storage\|thumbnails>`         | Opens app-related folders in a file browser.                                            |
| `rc`, `recent`                                                          | List, run or remove recently run commands.                                              |


## Common `search` and `keywords` command options

Both commands share the following options:

| shorthand, name \<arguments>    |                                                                                                                                                                                                                                                                                                                                         |
| :------------------------------ | :-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `channels` `<channels>`         | The space-separated channel IDs, handles, slugs, user names and/or URLs for either of those. Effectively searches the special 'Uploads' `playlists` of the given channels, which are sorted latest `uploaded` first.                                                                                                                     |
| `playlists` `<playlists>`       | The space-separated playlist IDs and/or URLs. Note that playlists have custom sorting - i.e. new videos may appear at the top, bottom or anywhere in between.                                                                                                                                                                           |
| `videos` `<videos>`             | The space-separated YouTube video IDs and/or URLs. Note that if the video ID starts with a dash, you have to quote it like "-1a2b3c4d5e" or use the entire URL to prevent it from being misinterpreted as a command option.                                                                                                             |
| `-m`, `--html`                  | If set, outputs the highlighted search result in an HTML file including hyperlinks for easy navigation. The output path can be configured in the `--out` parameter. Omitting it will save the file into the default `output` folder - named according to your search parameters. Existing files with the same name will be overwritten. |
| `-o`, `--out` `<out>`           | Writes the search results to a file, the format of which is either text or HTML depending on the `--html` flag. Supply either a file or folder path. If the path doesn't contain a file name, the file will be named according to your search parameters. Existing files with the same name will be overwritten.                        |
| `-s`, `--show` `<file\|folder>` | The output to open if a file was written.                                                                                                                                                                                                                                                                                               |
| `-rc`, `--recent`               | Unless set explicitly to `false`, saves this command into the recent command list to enable re-running it later. [default: True]                                                                                                                                                                                                        |


## `search`-only command options

In addition to the [Common `search` and `keywords` command options](#common-search-and-keywords-command-options), the `search` command features the following options.

| shorthand, name \<arguments>    |                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| :------------------------------ | :-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `-f`, `--for` `<for>`           | (REQUIRED) What to search for. Quote "multi-word phrases". Single words are matched exactly by default, ?fuzzy or with wild cards for s%ngle and multi* letters. Combine multiple & terms \| "phrases or queries" using `&` as logical *and* and ` \| ` as *or*. Use ( brackets \| for ) & ( complex \| expressions ). Words can have > order, appear ~ near to each other - or both, even with configurable ~3> proximity. You can restrict your search to the video `Title`, `Description`, `Keywords` and/or language-specific captions; e.g. `Title = "click bait" \| [English (auto-generated)] = howdy`. Learn more about the query syntax at https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/ . |
| `-p`, `--pad` `<pad>`           | How much context to pad a match in; i.e. the minimum number of characters of the original description or subtitle track to display before and after it. [default: 23]                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `-r`, `--order-by` `<order-by>` | Order the video search results by `uploaded` or `score` with `asc` for ascending. The default is descending (i.e. latest respectively highest first) and by `score`. Note that the order is only applied to the results with the search scope itself being limited by the `--skip` and `--take` parameters for playlists. Note also that for un-cached videos, this option is ignored in favor of outputting matches as soon as they're found - but simply repeating the search will hit the cache and return them in the requested order. [default: `score`]                                                                                                                                                               |


## Common `playlist` and `channel` scope options

Listing `keywords` and `search` don't operate on all videos contained in a `playlist` or `channel` scope by default. That makes searches on the latest videos of a channel or the top of a playlist quicker by reducing the number of videos that are loaded initially.
But you can change these defaults - both commands support the following options:

| shorthand, name \<arguments>           |                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| :------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `-sk`, `--skip` `<skip>`               | The number of videos to skip from the top of the included `channels` and `playlists`; effectively limiting the scope to the videos after it. You can specify a value for each included scope, `channels` before `playlists`, in the order they're passed. If you specify less values than scopes, the last value is used for remaining scopes. If left empty, 0 is used for all scopes.                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `-t`, `--take` `<take>`                | The number of videos to process from the top (or `--skip`-ed to part) of the included `channels` and `playlists`; effectively limiting the range. You can specify a value for each included scope, `channels` before `playlists`, in the order they're passed. If you specify less values than scopes, the last value is used for remaining scopes. If left empty, 50 is used for all scopes. You may want to gradually increase this to include all videos in the list while you're refining your query. Note that the special Uploads playlist of a channel is sorted latest `uploaded` first, but custom playlists may be sorted differently. Keep that in mind if you don't find what you're looking for and when using `--order-by` (which is only applied to the results) with `uploaded` on custom playlists. |
| `-ch`, `--cache-hours` `<cache-hours>` | The maximum ages of the included `channels` and `playlists` caches in hours before they're considered stale and the list of contained videos is refreshed. You can specify a value for each included scope, `channels` before `playlists`, in the order they're passed. If you specify less values than scopes, the last value is used for remaining scopes. If left empty, 24 is used for all scopes. Note this doesn't apply to the videos themselves because their contents rarely change after upload. Use `--clear-cache` to clear videos associated with a playlist or channel if that's what you're after.                                                                                                                                                                                                    |


## open, o

Opens app-related folders in a file browser.

| arguments               |                                                                                               |
| :---------------------- | :-------------------------------------------------------------------------------------------- |
| `<folder>` (**pos. 0**) | Required. The folder to open. Valid values: `app\|cache\|errors\|output\|storage\|thumbnails` |

with

| folder     | being the directory                                                                         |
| :--------- | :------------------------------------------------------------------------------------------ |
| app        | the app is running from                                                                     |
| cache      | used for caching channel, playlist and video info                                           |
| errors     | error logs are written to                                                                   |
| output     | output files are written to by default unless explicitly specified using the `--out` option |
| storage    | that hosts the `cache`, `errors` and `output` folders                                       |
| thumbnails | used for caching channel, playlist and video thumbnails downloaded by the UI                |


## clear-cache, clear

Deletes cached metadata and full-text indexes for `channels`, `playlists` and `videos`.

### Arguments

| name, position           |                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| :----------------------- | :---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `<scope>` (**pos. 0**)   | Required. The type of caches to delete. For `playlists` and `channels` this will include the associated `videos`. Valid values: `all\|channels\|playlists\|videos`                                                                                                                                                                                                                                                                                                            |
| `<aliases>` (**pos. 1**) | The space-separated IDs, URLs or aliases of elements in the `scope` to delete caches for. Can be used with every `scope` but `all` while supporting user names, channel handles and slugs besides IDs for `channels`. If not set, all elements in the specified `scope` are considered for deletion. Note that if the video ID starts with a dash, you have to quote it like "-1a2b3c4d5e" or use the entire URL to prevent it from being misinterpreted as a command option. |

### Options

| shorthand, name \<arguments>                  |                                                                                                                                                                                                                                                                                                                                              |
| :-------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `-l`, `--last-access`                         | The maximum number of days since the last access of a cache file for it to be excluded from deletion. Effectively only deletes old caches that haven't been accessed for this number of days. Ignored for explicitly set `aliases`.                                                                                                          |
| `-m`, `--mode` `<simulate\|summary\|verbose>` | The deletion mode; `summary` only outputs how many of what file type were deleted. `verbose` outputs the deleted file names as well as the summary. `simulate` lists all file names that would be deleted by running the command instead of deleting them. You can use this to preview the files that would be deleted. [default: `summary`] |


## release, r

List, browse and install other SubTubular releases.


### Commands

| shorthand, name \<arguments> |                                                                                                                        |
| :--------------------------- | :--------------------------------------------------------------------------------------------------------------------- |
| `l`, `list`                  | Lists available releases from https://github.com/h0lg/SubTubular/releases .                                            |
| `n`, `notes` `<version>`     | Opens the github release notes for a single release.                                                                   |
| `i`, `install` `<version>`   | Downloads a release from github and unzips it to the current installation folder while backing up the running version. |


### Arguments

| name, position           |                                                        |
| :----------------------- | :----------------------------------------------------- |
| `<version>` (**pos. 0**) | Required. The version number of a release or `latest`. |


# Examples & use cases

## Find specific parts of podcasts or other long-running videos

Scott Adams mentioned a psychological phenomenon named after a physicist on [his podcast](https://www.youtube.com/c/RealCoffeewithScottAdams) one of these days. Or did he say physician? What was its name again?

<pre>
SubTubular.Shell.exe <b>search videos</b> https://www.youtube.com/watch?v=<b>egeCYaIe21Y</b>
https://www.youtube.com/watch?v=<b>gDrFdxWNk8c</b> <b>--for</b> "physician | physicist" <b>--pad</b> 177
</pre>

or short

<pre>
SubTubular.Shell.exe <b>s videos</b> egeCYaIe21Y gDrFdxWNk8c <b>-f</b> "physician | physicist" <b>-p</b> 177
</pre>

gives you below result.

Note how the `--for|-f` argument is quoted [because it contains a `|` pipe](#writing-queries).

<pre>
15/08/2020 15:46 https://youtu.be/egeCYaIe21Y
  English (auto-generated)
    17:18  an eye on this aclu story because it seems they've turned bad now this
          is an example of a gel man amnesia i talk about this all the time gail mann was the name of a physicist who
          whenever he saw a story about physics he knew the story was wrong but then if he saw a story about some other
          topic he would say that's probably right    https://youtu.be/egeCYaIe21Y?t=1038
</pre>
<small>(turns out, it was the [Gell-Mann Amnesia effect](https://www.epsilontheory.com/gell-mann-amnesia/))</small>


## Search a playlist for mentions of a certain topic

The other day Styx mentioned some old book that describes the calcification of the pineal gland while predating the fluoridation of drinking water - apparently disproving the myth that it's caused by the fluoride.

Can we find it in his [occult literature playlist](https://www.youtube.com/playlist?list=PLe6Bc4vsmzwLiFQv1eh8oZe4uCkw-yYl7)? And would there be other mentions of fluoride in his reviews of old books?

<pre>
SubTubular.Shell.exe <b>search playlists</b> https://www.youtube.com/playlist?list=<b>PLe6Bc4vsmzwLiFQv1eh8oZe4uCkw-yYl7</b>
<b>--for</b> "( pineal ~ gland* & calcifi* ) | fluorid*" <b>--take</b> 500 <b>--pad</b> 90
</pre>

or shorter

<pre>
SubTubular.Shell.exe <b>s playlists</b> PLe6Bc4vsmzwLiFQv1eh8oZe4uCkw-yYl7
<b>-f</b> "( pineal ~ gland* & calcifi* ) | fluorid*" <b>-t</b> 500 <b>-p</b> 90
</pre>

both let you find below result.

But let's have a closer look at the **query** trailing the `--for|-f` - it searches
- **either** for occurrences of *pineal* **near** words **starting with** *gland* because we want to match *gland* or *glands* but only when occurring together with *pineal*; both words on their own may mean different things
  - **and** only if also something **starting with** *calcifi* (like *calcified* or *calcification*) is found in the same context
- **or** simply for anything **starting with** *fluorid* (like *fluoridation* or *fluoridated*)

<pre>
Occult Literature 14: Occultism For Beginners (Dower)
10/06/2016 22:00 https://youtu.be/Kf3LXznEka8
  English (auto-generated)
    00:56 it's it's categorizations according to more traditional occultism the use of the
          pituitary and <b>pineal glands</b> it also has one of the earliest mentions of the
          <b>calcification</b> of the <b>pineal gland</b> of any work that I've ever been able to find also
          proves because this predates <b>fluoridation</b> by almost 30 years proves the the
          <b>calcification</b> of the <b>pineal gland</b> was known long before <b>fluoride</b> was interjected into
          the average person's diet in the form of <b>fluoridated</b> water so New Agers beware you may
          not appreciate this work when you look at the date on it and then of course the
          treatise on    https://youtu.be/Kf3LXznEka8?t=56
</pre>

So apparently he spoke about Dower's *Occultism For Beginners* and no, there are no other fluoride-related mentions in his reviews.
<!-- TODO use case as a research tool: You have to prepare a talk and found channels or play lists on the topic? -->


## Using wild cards and exact matching in multi-word phrases

Since searching the occult playlist above, Little Jimmy listens to Heavy Metal (backwards of course), has been asking strange questions and generally has become very uppity. Talk around town is that he probably also does drugs, speaks in tongues and is into some sort of demon worship. They say he, his unfortunate twin Little Timmy and their friend Little Sally have been getting into all kinds of shenanigans lately.

### Windows CMD <!-- omit in toc -->
<pre>
> SubTubular.Shell.exe search channels Styxhexenhammer666 --for """little <b>?</b>jimmy"" | ""little sally""" --take 500 --pad 66
</pre>

### PowerShell <!-- omit in toc -->
<pre>
PS > .\SubTubular.Shell.exe search channels Styxhexenhammer666 --for '""little <b>?</b>jimmy"" | ""little sally""' --take 500 --pad 66
</pre>

### Bash <!-- omit in toc -->
<pre>
$ ./SubTubular.Shell.exe search channels Styxhexenhammer666 --for '"little ?jimmy" | "little sally"' --take 500 --pad 66
</pre>

Note how
- multi-word phrases are **quoted when nested** inside a quoted `--for|-f` argument on different shells
- **fuzzy-matching** *jimmy* using a *?* prefix will match "Little Jimmy" as well as "Little Timmy".

To prevent them from burning churches, we may have to restrict their access to harmful online content. Let's give them the old *Clockwork Orange* treatment and have them watch
[Bob Ross](https://www.youtube.com/@bobross_thejoyofpainting) paint *happy little* things and *beat the devil out* on a loop for a few days.

### Windows CMD <!-- omit in toc -->
<pre>
> SubTubular.Shell.exe <b>search channels</b> https://www.youtube.com/@<b>bobross_thejoyofpainting</b>
<b>--for</b> "[English (auto-generated)]= ( ""beat the devil out"" | ""happy little *"" )" <b>--take</b> 500 <b>--pad</b> 30
</pre>

or shorter

<pre>
> SubTubular.Shell.exe <b>s channels</b> bobross_thejoyofpainting
<b>-f</b> "[English (auto-generated)]= ( ""beat the devil out"" | ""happy little *"" )" <b>-t</b> 500 <b>-p</b> 30
</pre>

### PowerShell <!-- omit in toc -->
<pre>
PS > .\SubTubular.Shell.exe <b>search channels</b> https://www.youtube.com/@<b>bobross_thejoyofpainting</b>
<b>--for</b> '[English (auto-generated)]= ( ""beat the devil out"" | ""happy little *"" )' <b>--take</b> 500 <b>--pad</b> 30
</pre>

or shorter

<pre>
PS > .\SubTubular.Shell.exe <b>s channels</b> bobross_thejoyofpainting
<b>-f</b> '[English (auto-generated)]= ( ""beat the devil out"" | ""happy little *"" )' <b>-t</b> 500 <b>-p</b> 30
</pre>

### Bash <!-- omit in toc -->
<pre>
$ ./SubTubular.Shell.exe <b>search channels</b> https://www.youtube.com/@<b>bobross_thejoyofpainting</b>
<b>--for</b> '[English (auto-generated)]= ( "beat the devil out" | "happy little *" )' <b>--take</b> 500 <b>--pad</b> 30
</pre>

or shorter

<pre>
$ ./SubTubular.Shell.exe <b>s channels</b> bobross_thejoyofpainting
<b>-f</b> '[English (auto-generated)]= ( "beat the devil out" | "happy little *" )' <b>-t</b> 500 <b>-p</b> 30
</pre>

will fill their prescription with results like below.

Note how the `[English (auto-generated)]=(...)` expression excludes matches in title, description or keywords - since those wouldn't help our troubled kids.

<pre>
"Beat the devil out of it, and we're ready."
10/10/2022 22:00 https://youtu.be/D_xamByJsYs
  English (auto-generated)
    00:13 put the dark on clean the brush and <b>beat the devil out</b> of it
          and we're ready    https://youtu.be/D_xamByJsYs?t=13

Best of Clouds (Part 1) | The Joy of Painting with Bob Ross
12/05/2022 22:00 https://youtu.be/y5OXoEtcen8
  English
    01:38 Right there we have just another <b>happy little cloud</b>. They just float
          around here and have a good time all day.    https://youtu.be/y5OXoEtcen8?t=98
    04:16 Then. (brush rattles) (chuckles) Just <b>beat the devil out</b> of it. There. And sometimes I'll take
          the brush and go across    https://youtu.be/y5OXoEtcen8?t=256
    13:40 Now maybe, maybe in our world, there's just a <b>happy little cloud</b> that lives up here.
          This is pure midnight black, pure black.    https://youtu.be/y5OXoEtcen8?t=820
    17:28 Okay, maybe in our world there's a <b>happy little cloud</b>. Just sort of floats
          around in the sky up here    https://youtu.be/y5OXoEtcen8?t=1048
    18:19 So we'll give him one, lives right there. Just a <b>happy little guy</b>.
          In my world, everything is happy. So we have <b>happy little clouds</b> and happy trees.
          All right, there we go.    https://youtu.be/y5OXoEtcen8?t=1099
</pre>


## Search a channel for specific content

I might have gazed into the abyss for a little too long and now I need a deep breath, some unclenching and a refresher on the importance of free speech. [Russell Brand](https://www.youtube.com/@RussellBrand) may be able to help me with that - he seems to enjoy making use of it. Let's see if we can pick his thoughts on the topic out of the whirlwind of praise for our benevolent elites and trusted institutions.

### Windows CMD <!-- omit in toc -->
<pre>
> SubTubular.Shell.exe <b>search channels</b> https://www.youtube.com/@<b>RussellBrand</b>
<b>--for</b> """freedom of speech"" | ""free speech"" | censorship | ""cancel culture"""
<b>--take</b> 500 <b>--pad</b> 40
</pre>

or short

<pre>
> SubTubular.Shell.exe <b>s channels</b> RussellBrand
<b>-f</b> """freedom of speech"" | ""free speech"" | censorship | ""cancel culture"""
<b>-t</b> 500 <b>-p</b> 40
</pre>

### PowerShell <!-- omit in toc -->
<pre>
PS > .\SubTubular.Shell.exe <b>search channels</b> https://www.youtube.com/@<b>RussellBrand</b>
<b>--for</b> '""freedom of speech"" | ""free speech"" | censorship | ""cancel culture""'
<b>--take</b> 500 <b>--pad</b> 40
</pre>

or short

<pre>
PS > .\SubTubular.Shell.exe <b>s channels</b> RussellBrand
<b>-f</b> '""freedom of speech"" | ""free speech"" | censorship | ""cancel culture""'
<b>-t</b> 500 <b>-p</b> 40
</pre>

### Bash <!-- omit in toc -->
<pre>
$ ./SubTubular.Shell.exe <b>search channels</b> https://www.youtube.com/@<b>RussellBrand</b>
<b>--for</b> '"freedom of speech" | "free speech" | censorship | "cancel culture"'
<b>--take</b> 500 <b>--pad</b> 40
</pre>

or short

<pre>
$ ./SubTubular.Shell.exe <b>s channels</b> RussellBrand
<b>-f</b> '"freedom of speech" | "free speech" | censorship | "cancel culture"'
<b>-t</b> 500 <b>-p</b> 40
</pre>

will let you find something like the following.
Note that title, description and keywords are matched as well as subtitles.

<pre>
Who Benefits From Online <b>Censorship</b>?
02/04/2022 22:00 https://youtu.be/CoUW0iR8ewU
  in description: a new bill to regulate online speech.
                  #<b>Censorship</b> #Canada #FreeSpeech

                  References
                  https://reclaimthenet.org/canadas-internet-<b>censorship</b>-bill-is-a-major-threat-to-<b>free-speech</b>-online/

                  https://chrishedges.substack.c
  in keywords: <b>censorship</b>
  English (auto-generated)
    00:00 <b>censorship</b> it's everywhere whether it's russia today all canadians or me
          <b>censorship</b> is back in fashion why and who does it benefit is it the vulnerable
          https://youtu.be/CoUW0iR8ewU?t=0
    00:48 controversial bc11 otherwise known as the internet <b>censorship</b> bill i can see
          why they want to call it fc11 sounds a    https://youtu.be/CoUW0iR8ewU?t=48
    02:53 speech shut up the main criticism the bill has faced from a flurry of
          <b>free speech</b> advocates of various ideological and political
          persuasions is that the    https://youtu.be/CoUW0iR8ewU?t=173
</pre>


## Exploring a channel or playlist via its keywords

What else has Russell Brand been talking about recently on his channel?

<pre>
SubTubular.Shell.exe <b>search channels</b> https://www.youtube.com/@<b>RussellBrand</b>
<b>--keywords</b> <b>--take</b> 100
</pre>

or short

<pre>
SubTubular.Shell.exe <b>s channels</b> RussellBrand <b>-k</b> <b>-t</b> 100
</pre>

will look at the keywords the top 100 videos of the searched playlist are tagged with and list them with their number of occurrences, most used first.

<pre>
100x News | 100x politics | 8x pandemic | 6x covid | 5x Putin | 5x Ukraine | 4x cold war
4x fauci | 4x invasions | 4x latest news | 4x military | 4x military industrial complex
4x NATO | 4x news | 4x Russia | 4x russia ukraine war | 4x the cold war | 4x ukraine 2014
4x ukraine crisis | 4x Vladimir Putin | 4x War | 4x world war | 4x World War 3 | 4x WW3
4x WWIII | 3x biden | 3x bill gates | 3x Cold War | 3x nord stream | 3x Nord Stream pipeline
3x russian army | 3x ukraine russia war | 3x Ukraine war | 3x vaccines | 3x WEF
2x big tech | 2x censorship | 2x china | 2x chinese | 2x coronavirus | 2x cover-up
2x covid-19 | 2x elon | 2x Elon Musk | 2x follow the science | 2x Institute of Virology
2x investigation | 2x jabs | 2x joe biden | 2x lab | 2x lab leak | 2x leak | 2x leaked
2x market | 2x new prime minister uk | 2x outbreak | 2x Peter Daszak | 2x putin
2x rachael maddow | 2x rishi | 2x rishi sunak | 2x science | 2x scientists
2x stop the spread | 2x theory | 2x trump | 2x ukraine | 2x ukraine war | 2x unvaccinated
2x vaccinated | 2x vaccine | 2x Virology | 2x virus | 2x war | 2x wet market
</pre>


## Find material for a supercut of a phrase

I have here a pile of rocks that needs grinding. Let's make a supercut of Jörg Sprave's laughter. And while we're at it, *let me show you its features*:

### Windows CMD <!-- omit in toc -->
<pre>
> SubTubular.Shell.exe <b>search channels</b> https://www.youtube.com/user/<b>JoergSprave</b>
<b>--for</b> "haha | laugh* | ""let me show you its features""" <b>--take</b> 100 <b>--cache-hours</b> 0
<b>--order-by</b> uploaded asc <b>--html</b> <b>--out</b> "path/to/my output file.html" <b>--show</b> file
</pre>

or short

<pre>
> SubTubular.Shell.exe <b>s channels</b> JoergSprave <b>-f</b> "haha | laugh* | ""let me show you its features"""
<b>-t</b> 100 <b>-ch</b> 0 <b>-r</b> uploaded asc <b>-m</b> <b>-o</b> "path/to/my output file.html" <b>-s</b> file
</pre>

### PowerShell <!-- omit in toc -->
<pre>
PS > .\SubTubular.Shell.exe <b>search channels</b> https://www.youtube.com/user/<b>JoergSprave</b>
<b>--for</b> 'haha | laugh* | ""let me show you its features""' <b>--take</b> 100 <b>--cache-hours</b> 0
<b>--order-by</b> uploaded asc <b>--html</b> <b>--out</b> "path/to/my output file.html" <b>--show</b> file
</pre>

or short

<pre>
PS > .\SubTubular.Shell.exe <b>s channels</b> JoergSprave <b>-f</b> 'haha | laugh* | ""let me show you its features""'
<b>-t</b> 100 <b>-ch</b> 0 <b>-r</b> uploaded asc <b>-m</b> <b>-o</b> "path/to/my output file.html" <b>-s</b> file
</pre>

### Bash <!-- omit in toc -->
<pre>
$ ./SubTubular.Shell.exe <b>search channels</b> https://www.youtube.com/user/<b>JoergSprave</b>
<b>--for</b> 'haha | laugh* | "let me show you its features"' <b>--take</b> 100 <b>--cache-hours</b> 0
<b>--order-by</b> uploaded asc <b>--html</b> <b>--out</b> "path/to/my output file.html" <b>--show</b> file
</pre>

or short

<pre>
$ ./SubTubular.Shell.exe <b>s channels</b> JoergSprave <b>-f</b> 'haha | laugh* | "let me show you its features"'
<b>-t</b> 100 <b>-ch</b> 0 <b>-r</b> uploaded asc <b>-m</b> <b>-o</b> "path/to/my output file.html" <b>-s</b> file
</pre>

thankfully at any given time will yield results like you find below.

Note how
- `--take|-t 100` only searches the top 100 videos in the Uploads playlist of the channel
- `--cache-hours|-ch 0` disables playlist caching to make sure we get the freshest laughs
- `--order-by|-r uploaded asc` will sort the results by `uploaded` date instead of score and `asc`ending (latest last) instead of descending (latest first)
- `--html|-m` will generate a HTML output file including time-stamped hyperlinks to the found results
- `--out|-o "path/to/my output file.html"` will save the output file to a custom path instead of the default output folder; the path being quoted because it contains spaces
- `--show|-s file` will open the output file after it has been written so you don't have to navigate to it

<pre>
The 200 Joule Repeating Rubber X-Bow Project!
18/05/2022 22:00 https://youtu.be/iiUOVlnj65w
  English (auto-generated)
    00:16 today because it's shooting <b>let me show you its features</b> repeating crossbows
          like the adder the stinger and    https://youtu.be/iiUOVlnj65w?t=16

The Inventor who wouldn't give up...
01/06/2022 22:00 https://youtu.be/JO-A3Z6S3b4
  English (auto-generated)
    01:47 accidents like the last one [<b>Laughter</b>] so after i had repaired it
          https://youtu.be/JO-A3Z6S3b4?t=107
</pre>


# Tips & best practices

## Writing queries

To start with, you'll want to **get familiar with the syntax of the shell** you're using - at least to the degree that you know how to **quote arguments**. There are [examples](#examples--use-cases) above to give you an idea. You'll end up quoting the `--for|-f` parameter a lot because some control characters used by the LIFTI query syntax will conflict with control characters of your shell.
The best example for this is the `|` pipe, which LIFTI uses as an [OR operator](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/#or-) -  but on the most common shells forwards the output of a command preceding it to a command trailing it.
Since we don't want that, we'll have to quote any query that contains an OR pipe - and maybe escape nested quotes depending on the shell.

Next, learn the features of the [LIFTI **query syntax**](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/) and try them out one by one until you understand them. It helps to do that with a channel, playlist or videos you know a bit of the content of - so you know what you *should* find.

You'll probably want to use an iterative process for designing your full-text queries. Start with a simple one and see what it matches, then progressively tweak it until you're happy with the results.
Keep in mind that not immediately finding what your looking for in a playlist could also just mean you have to increase the `--take`n number of videos to search.


## Searching auto-generated subtitles

If you can't seem to find what you're looking for, here are some things to keep in mind:

- Make sure the videos you search have subtitles. Not all do. Or at least not immediately. Allow for some time before the auto-generated subtitles of newly-uploaded videos are available.
- Try fuzzy matching for names and words with different or uncommon spellings.
- Keep your multi-word phrases short or use [nearness expressions](https://mikegoatly.github.io/lifti/docs/searching/lifti-query-syntax/#near-following--and-n). Make use of wild cards and fuzzy matching. Otherwise, only exact matches are returned - so the longer your phrase, the less likely it is to match anything.
- Omit punctuation in the original text (dots, commas, question marks and double quotes around citations). Those will be regarded as punctuation, not searchable content.
- Note that auto-generated subtitles may not always make sense, semantically speaking. Similar sounding words may be misunderstood, especially for speakers with poor pronunciation, high throughput, an accent or simply due to background noise. A statement about *defense* could for example easily be misinterpreted as being about *the fence*.
- You'll find that the speech recognition algorithm will replace
  - `[Music]` `[Laughter]` and `[Applause]` with [these placeholders](https://research.google/blog/adding-sound-effect-information-to-youtube-captions/) - translated into the caption track language. Escape e.g. like `\[Laughter\]`.
  - words YouTube considers inappropriate with `[ __ ]` depending on [the channel setting](https://support.google.com/youtube/answer/6373554?hl=en#zippy=%2Cpotentially-inappropriate-words-in-automatic-captions).

Feel free to contribute your own best practices in the [issues](https://github.com/h0lg/SubTubular/issues).


# Fair use

Do **not** use this software with the intent of infringing on any creator's freedom of speech or any viewer's freedom of choice.

Specifically, you may **not** use this software or its output to target content for flagging, banning or demonetizing.

Those to whom this limitation applies, should feel encouraged to explore the origins of their right to censor third party conversation and come back another day with better intentions <3
