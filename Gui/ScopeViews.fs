namespace SubTubular.Gui

open System
open Fabulous.Avalonia
open SubTubular
open type Fabulous.Avalonia.View

module ScopeDiscriminators =
    let (|IsChannel|IsPlaylist|IsVideos|) (t: Type) =
        match t with
        | _ when t = typeof<ChannelScope> -> IsChannel
        | _ when t = typeof<PlaylistScope> -> IsPlaylist
        | _ when t = typeof<VideosScope> -> IsVideos
        | _ -> failwith ("unknown scope type " + t.FullName)

    let (|Channel|Playlist|Videos|) (scope: CommandScope) =
        match scope with
        | :? ChannelScope as channel -> Channel channel
        | :? PlaylistScope as playlist -> Playlist playlist
        | :? VideosScope as videos -> Videos videos
        | _ -> failwith $"unsupported {nameof CommandScope} type on {scope}"

    let (|PlaylistLike|Vids|) (scope: CommandScope) =
        match scope with
        | :? PlaylistLikeScope as playlist -> PlaylistLike playlist
        | :? VideosScope as videos -> Vids videos
        | _ -> failwith $"unsupported {nameof CommandScope} type on {scope}"

module ScopeViews =
    open ScopeDiscriminators

    let channelInfo channel =
        TextBlock(Icon.channel + channel).smallDemoted ()

    let getIcon (t: Type) =
        match t with
        | IsVideos -> Icon.video
        | IsPlaylist -> Icon.playlist
        | IsChannel -> Icon.channel

    let displayType (t: Type) withKeyBinding =
        getIcon t
        + if withKeyBinding then "_" else ""
        + match t with
          | IsVideos -> "videos"
          | IsPlaylist -> "playlist"
          | IsChannel -> "channel"

    let progressText text = TextBlock(text).right().smallDemoted ()
