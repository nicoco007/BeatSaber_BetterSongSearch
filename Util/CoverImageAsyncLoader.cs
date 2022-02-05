﻿using BetterSongSearch.UI;
using SongDetailsCache.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BetterSongSearch.Util {
	class CoverImageAsyncLoader : IDisposable {
		static Dictionary<string, Sprite> _spriteCache;

		public CoverImageAsyncLoader() {
			_spriteCache ??= new Dictionary<string, Sprite>();

			client = new HttpClient(new HttpClientHandler() {
				AutomaticDecompression = DecompressionMethods.GZip,
				AllowAutoRedirect = true
			}) {
				Timeout = TimeSpan.FromSeconds(5)
			};

			client.DefaultRequestHeaders.Add("User-Agent", "BetterSongSearch/" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
		}

		static HttpClient client = null;

		public async Task<Sprite> LoadAsync(Song song, CancellationToken token) {
			var path = song.coverURL;

			if(PluginConfig.Instance.downloadUrlOverride.Length != 0)
				path = $"{PluginConfig.Instance.downloadUrlOverride}/{song.hash.ToLowerInvariant()}.jpg";

			if(_spriteCache.TryGetValue(path, out Sprite sprite))
				return sprite;

			try {
				using(var resp = await client.GetAsync(path, HttpCompletionOption.ResponseContentRead, token)) {
					if(resp.StatusCode == HttpStatusCode.OK) {
						var imageBytes = await resp.Content.ReadAsByteArrayAsync();

						if(_spriteCache.TryGetValue(path, out sprite))
							return sprite;

						sprite = BeatSaberMarkupLanguage.Utilities.LoadSpriteRaw(imageBytes);
						sprite.texture.wrapMode = TextureWrapMode.Clamp;
						_spriteCache[path] = sprite;

						return sprite;
					}
				}
			} catch { }

			return SongCore.Loader.defaultCoverImage;
		}

		public void Dispose() {
			foreach(var x in _spriteCache.Keys.ToArray()) {
				if(_spriteCache[x] == BSSFlowCoordinator.songListView.selectedSongView.coverImage.sprite)
					continue;

				_spriteCache.Remove(x);
			}

			client.CancelPendingRequests();
			client.Dispose();
			client = null;
		}
	}
}
