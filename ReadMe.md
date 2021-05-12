# SubTubular <!-- omit in toc -->

A **full-text search** for **[YouTube](https://www.youtube.com/) subtitles** and **video metadata** with a **command line** interface.

<img src="./SubTubular.ico" />

- [Overview](#overview)
- [Commands](#commands)
  - [search-videos](#search-videos)
  - [search-playlist](#search-playlist)
  - [search-channel](#search-channel)
  - [search-user](#search-user)
  - [clear-cache](#clear-cache)
- [Fair use](#fair-use)
- [Examples & use cases](#examples--use-cases)
  - [Find specific parts of podcasts or other long-running videos](#find-specific-parts-of-podcasts-or-other-long-running-videos)
  - [Search a diversified channel for content on a certain topic](#search-a-diversified-channel-for-content-on-a-certain-topic)
  - [Find material for a supercut of a certain word or phrase](#find-material-for-a-supercut-of-a-certain-word-or-phrase)
- [Tips & best practices for auto-generated subtitles](#tips--best-practices-for-auto-generated-subtitles)

# Overview

## Searches <!-- omit in toc -->
- video **title**, **description**, **keywords** and **subtitles** (also called **closed captions**/**CC** or **transcript**)
- in the the scope of one or **multiple videos**, a **playlist**, **channel** or **user**
- supporting **multiple terms** and **multi-word phrases** (combining them via boolean OR; i.e. logical either/or)
- matching phrases **spanning multiple captions**
- ignoring the case of the search terms

## returning <!-- omit in toc -->
- a list of search results with **highlighted** matches
- including **time-stamped video links** to the matched part of the video

## caching <!-- omit in toc -->
- searchable **video metadata** and **subtitles** in **all available languages**
- **videos in playlists**, channels or user accounts **for a configurable time**
- in your **local user profile**, i.e.
  - %AppData%\Roaming on Windows
  - ~/.config on Linux and macOS
- until you **explicitly clear** it
- so that **subsequent searches** on the same scope can be done **offline** and are way **faster** than the first one

## requiring <!-- omit in toc -->
- **no installation** except for [**.NET Core 3.1**](https://dotnet.microsoft.com/download/dotnet-core)
- **no YouTube login**
- **few resources**, as this project was partly an excercise in the [newer async features of .NET Core 3.0 and C# 8 for concurrent, non-blocking operations](https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/november/csharp-iterating-with-async-enumerables-in-csharp-8)

## thanks to <!-- omit in toc -->
- [**YoutubeExplode**](https://github.com/Tyrrrz/YoutubeExplode) licensed under [LGPL 3](https://github.com/Tyrrrz/YoutubeExplode/blob/master/License.txt) for doing a better job at getting the relevant data off of YouTube's public web API than YouTube's own [Data API v3](https://developers.google.com/youtube/v3/) is able to do at the time of writing. And for not requiring a clunky app registration and user authorization for every bit of data on top of that. A real game-changer!
  - including [AngleSharp](https://github.com/AngleSharp/AngleSharp) licensed under [MIT](https://github.com/AngleSharp/AngleSharp/blob/master/LICENSE)
- [**CommandLineParser**](https://github.com/commandlineparser/commandline) licensed under [MIT](https://github.com/commandlineparser/commandline/blob/master/License.md) for elegantly parsing and validating command line arguments and generating help text

## **not** providing <!-- omit in toc -->
- subtitle download in any common, reusable format (although that could probably be added quite easily)
- fuzzy search. Only exact matches are returned.

# Commands


## search-videos

Searches the {videos} {for} the specified terms.

|              |                                                                                                  |
| :----------- | :----------------------------------------------------------------------------------------------- |
| value pos. 0 | Required. The space-separated YouTube video IDs and/or URLs.                                     |
| -f, --for    | Required. What to search for. Quote "multi-word phrases" and "separate,multiple terms,by comma". |


## search-playlist

Searches the {top} n videos from the {playlist} {for} the specified terms.

|                  |                                                                                                                                                                                                                |
| :--------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| value pos. 0     | Required. The playlist ID or URL.                                                                                                                                                                              |
| -f, --for        | Required. What to search for. Quote "multi-word phrases" and "separate,multiple terms,by comma".                                                                                                               |
| -t, --top        | (Default: 50) The number of videos to return from the top of the playlist. The special Uploads playlist of a channel or user are sorted latest uploaded first, but custom playlists may be sorted differently. |
| -h, --cachehours | (Default: 24) The maximum age of a playlist cache in hours before it is considered stale and the videos in it are refreshed.                                                                                   |


## search-channel

Searches the {top} n videos from the Uploads playlist of the {channel} {for} the specified terms. This is a glorified search-playlist.

|                  |                                                                                                                                                                                                                |
| :--------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| value pos. 0     | Required. The channel ID or URL.                                                                                                                                                                               |
| -f, --for        | Required. What to search for. Quote "multi-word phrases" and "separate,multiple terms,by comma".                                                                                                               |
| -t, --top        | (Default: 50) The number of videos to return from the top of the playlist. The special Uploads playlist of a channel or user are sorted latest uploaded first, but custom playlists may be sorted differently. |
| -h, --cachehours | (Default: 24) The maximum age of a playlist cache in hours before it is considered stale and the videos in it are refreshed.                                                                                   |


## search-user

Searches the {top} n videos from the Uploads playlist of the {user}'s channel {for} the specified terms. This is a glorified search-playlist.

|                  |                                                                                                                                                                                                                |
| :--------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| value pos. 0     | Required. The user name or URL.                                                                                                                                                                                |
| -f, --for        | Required. What to search for. Quote "multi-word phrases" and "separate,multiple terms,by comma".                                                                                                               |
| -t, --top        | (Default: 50) The number of videos to return from the top of the playlist. The special Uploads playlist of a channel or user are sorted latest uploaded first, but custom playlists may be sorted differently. |
| -h, --cachehours | (Default: 24) The maximum age of a playlist cache in hours before it is considered stale and the videos in it are refreshed.                                                                                   |


## clear-cache

Clears cached user, channel, playlist and video info.


# Fair use

Do **not** use this software with the intent of infringing on any creator's freedom of speech or any viewer's freedom of choice.

Specifically, you may **not** use this software or its output to target content for flagging, banning or demonitizing.

Those to whom this limitation applies, should feel encouraged to explore the origins of their right to censor third party conversation and come back another day with better intentions <3


# Examples & use cases

## Find specific parts of podcasts or other long-running videos

Scott Adams mentioned this psychological phenomenon named after a physicist one of these days. Or did he say physician? What was its name again?

<pre>
> SubTubular <b>search-videos</b> https://www.youtube.com/watch?v=egeCYaIe21Y https://www.youtube.com/watch?v=gDrFdxWNk8c <b>--for</b> physician,physicist
</pre>

or short

<pre>
> SubTubular <b>search-videos</b> egeCYaIe21Y gDrFdxWNk8c <b>-f</b> physician,physicist
</pre>

gives you

<pre>
15/08/2020 15:34 https://youtu.be/egeCYaIe21Y
  English (auto-generated)
    17:31 gail mann was the name of a <b>physicist</b>    https://youtu.be/egeCYaIe21Y?t=1051
</pre>
<small>(turns out, it was the [Gell-Mann Amnesia effect](https://www.epsilontheory.com/gell-mann-amnesia/))</small>


## Search a diversified channel for content on a certain topic

I might have gazed into the abyss for a little too long and now I need a deep breath, some unclenching and a refresher on the importance of free speech. I know StyxHexenhammer has a lot to say on the matter - if I can dig it out of the gardening content and occult literature.

<pre>
> SubTubular <b>search-channel</b> https://www.youtube.com/channel/UC0rZoXAD5lxgBHMsjrGwWWQ <b>--for</b> "free speech,censorship,cancel culture,cancelculture,freespeech" <b>--top</b> 500
</pre>

or short

<pre>
> SubTubular <b>search-channel</b> UC0rZoXAD5lxgBHMsjrGwWWQ <b>-f</b> "free speech,censorship,cancel culture,cancelculture,freespeech" <b>-t</b> 500
</pre>

Note that title, description and keywords are matched as well as subtitles.

<pre>
08/10/2020 07:58 https://youtu.be/xoZOMpoeots
  in description: #Qanon #<b>Censorship</b>
  in keywords: <b>censorship</b>, tech <b>censorship</b>, #<b>censorship</b>
  English (auto-generated)
    03:58 in extreme <b>free speech</b> which means    https://youtu.be/xoZOMpoeots?t=238
    04:00 <b>free speech</b> i'm an extremist when it    https://youtu.be/xoZOMpoeots?t=240

06/10/2020 08:42 https://youtu.be/8TysuANlPic
  in title: <b>Cancel Culture</b> Comes for the CEO of the Babylon Bee
  in keywords: <b>cancel culture</b>, #<b>cancelculture</b>
  English (auto-generated)
    01:07 why is it that <b>cancel culture</b> would come    https://youtu.be/8TysuANlPic?t=67
    06:31 and <b>cancel culture</b> is something that's    https://youtu.be/8TysuANlPic?t=391
    06:50 <b>cancel culture</b> because it reminds them    https://youtu.be/8TysuANlPic?t=410
    08:35 with <b>censorship</b> whether government    https://youtu.be/8TysuANlPic?t=515
    08:57 <b>cancel culture</b> it's something that gets    https://youtu.be/8TysuANlPic?t=537
</pre>


## Find material for a supercut of a certain word or phrase

I have here a pile of rocks that needs grinding. Also, the Middle East could do with some peace. Let's make a supercut of Jörg Sprave's laugh. And while we're at it, let me show you its features:

<pre>
> SubTubular <b>search-user</b> https://www.youtube.com/user/JoergSprave <b>--for</b> "haha,let me show you its features" <b>--top</b> 100 <b>--cachehours</b> 0 #disable cache to make sure I get the freshest laughs
</pre>

or short

<pre>
> SubTubular <b>search-user</b> JoergSprave <b>-f</b> "haha,let me show you its features" <b>-t</b> 100 <b>-h</b> 0
</pre>

thankfully at any given time will yield something like

<pre>
18/07/2020 16:52 https://youtu.be/WOFNUPH2hUY
  English (auto-generated)
    01:50 cutter like a mini pizza cutter <b>haha</b>ha I    https://youtu.be/WOFNUPH2hUY?t=110
    24:02 <b>haha</b><b>haha</b> so it may be a lot of things    https://youtu.be/WOFNUPH2hUY?t=1442

13/07/2020 16:40 https://youtu.be/52miCqsi7lo
  English (auto-generated)
    37:38 upper band <b>haha</b>    https://youtu.be/52miCqsi7lo?t=2258

11/07/2020 12:18 https://youtu.be/nyze8uJovdo
  English (auto-generated)
    00:21 <b>let me show you its features</b> I know I    https://youtu.be/nyze8uJovdo?t=21

21/06/2020 21:03 https://youtu.be/BF_OuEba3a4
  English (auto-generated)
    00:39 boat <b>let me show you its features</b>    https://youtu.be/BF_OuEba3a4?t=39
    24:31 <b>haha</b>ha victory and now of course coconut    https://youtu.be/BF_OuEba3a4?t=1471
    28:19 <b>haha</b>ha bye bye well the week is setting    https://youtu.be/BF_OuEba3a4?t=1699
    39:18 <b>haha</b>ha and it is also clear that Odin    https://youtu.be/BF_OuEba3a4?t=2358
</pre>


# Tips & best practices for auto-generated subtitles

If you can't seem to find what you're looking for, here's some things to keep in mind:

- Make sure the videos you search have subtitles. Not all do. Or at least not immediately. Allow for some time before the auto-generated subtitles of newly-uploaded videos are available.
- Keep your multi-word phrases short. Only exact matches are returned - so the longer and more complex your query, the less likely it is to match anything.
- Omit punctuation (dots and commas). As of writing this, the auto-generated subtitles are not structured into sentences.
- Don't overestimate YouTube's speech recognition algorithm (yet). Auto-generated subtitles don't always make sense, semantically speaking. Similar sounding words will be misunderstood, especially for speakers with poor pronunciation, high throughput, an accent or simply due to background noise. A statement about *defense* could for example easily be misunderstood as being about a *fence*, because the first syllable is often de-emphasized - something a human mind does not struggle with, reading a lot of meaning out of the context of a statement.
- You'll find that the speech recognition algorithm will replace
  - inaudible words with *?* and
  - swear words with *[ __ ]* .

Feel free to contribute your own best practices in the [issues](https://github.com/h0lg/SubTubular/issues).
