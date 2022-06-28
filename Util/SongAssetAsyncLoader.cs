﻿using BetterSongSearch.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SongDetailsCache.Structs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace BetterSongSearch.Util {
	class SongAssetAsyncLoader : IDisposable {
		static Dictionary<string, Sprite> _spriteCache;
		static Dictionary<string, AudioClip> _previewCache;

		public static readonly string UserAgent = "BetterSongSearch/" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

		static HttpClient client = null;

		public SongAssetAsyncLoader() {
			_spriteCache ??= new Dictionary<string, Sprite>();
			_previewCache ??= new Dictionary<string, AudioClip>();

			client = new HttpClient(new HttpClientHandler() {
				AutomaticDecompression = DecompressionMethods.GZip,
				AllowAutoRedirect = true
			}) {
				Timeout = TimeSpan.FromSeconds(5)
			};

			client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
		}

		async Task<string> ApiRequest(string key, Func<JObject, string> valueGetter, CancellationToken token) {
			var baseUrl = PluginConfig.Instance.apiUrlOverride;

			if(baseUrl.Length == 0)
				baseUrl = BeatSaverRegionManager.detailsDownloadUrl;

			using(var resp = await client.GetAsync($"{baseUrl}/{key.ToLowerInvariant()}", HttpCompletionOption.ResponseHeadersRead, token)) {
				if(resp.StatusCode != HttpStatusCode.OK)
					throw new Exception($"Unexpected HTTP response: {resp.StatusCode} {resp.ReasonPhrase}");

				using(var reader = new StreamReader(await resp.Content.ReadAsStreamAsync()))
				using(var jsonReader = new JsonTextReader(reader)) {
					var ser = new JsonSerializer();

					return valueGetter(ser.Deserialize<JObject>(jsonReader));
				}
			}
		}

		public Task<string> GetSongDescription(string key, CancellationToken token) {
			return ApiRequest(key, (x) => x.GetValue("description").Value<string>(), token);
		}
		public Task<string> GetPreviewURL(string key, CancellationToken token) {
			return ApiRequest(key, (x) => x.SelectToken("versions[0].coverURL", false).Value<string>(), token);
		}

		public async Task<Sprite> LoadCoverAsync(Song song, CancellationToken token) {
			var path = PluginConfig.Instance.coverUrlOverride;

			if(path.Length == 0)
				path = BeatSaverRegionManager.coverDownloadUrl;

			path += $"/{song.hash.ToLowerInvariant()}.jpg";

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

		public Task<AudioClip> LoadPreviewAsync(Song song, CancellationToken token) {
			var path = PluginConfig.Instance.previewUrlOverride;

			if(path.Length == 0)
				path = BeatSaverRegionManager.previewDownloadUrl;

			path += $"/{song.hash.ToLowerInvariant()}.mp3";

			if(_previewCache.TryGetValue(path, out AudioClip ac))
				return Task.FromResult(ac);

			var tcs = new TaskCompletionSource<AudioClip>();

			IEnumerator ucrap() {
				var completed = false;

				using(var www = UnityWebRequestMultimedia.GetAudioClip(path, AudioType.MPEG)) {
					token.Register(() => {
						if(!completed && !www.isDone)
							www.Abort();
					});

					www.SetRequestHeader("User-Agent", UserAgent);

					yield return www.SendWebRequest();

					if(!www.isHttpError) {
						try {
							var clip = DownloadHandlerAudioClip.GetContent(www);

							if(clip != null && clip.loadState == AudioDataLoadState.Loaded) {
								_previewCache.Add(path, clip);

								tcs.SetResult(clip);
								completed = true;
								yield break;
							}
						} catch { }
					}
					tcs.SetException(new Exception());
					completed = true;
				}
			};

			SharedCoroutineStarter.instance.StartCoroutine(ucrap());

			return tcs.Task;
		}

		public void Dispose() {
			foreach(var x in _spriteCache.Keys.ToArray()) {
				var s = _spriteCache[x];

				if(s == BSSFlowCoordinator.songListView.selectedSongView.coverImage.sprite)
					continue;

				_spriteCache.Remove(x);

				GameObject.DestroyImmediate(s.texture);
				GameObject.DestroyImmediate(s);
			}

			foreach(var x in _previewCache.Values)
				GameObject.Destroy(x);

			_previewCache.Clear();

			client.CancelPendingRequests();
			client.Dispose();
			client = null;
		}
	}
}
