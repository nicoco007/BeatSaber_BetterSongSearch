﻿using BetterSongSearch.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterSongSearch.Util {
	static class WeightedSongSearch {
		struct xd {
			public SongSearchSong song;
			public float searchWeight;
			public float sortWeight;
		}

		public static IEnumerable<SongSearchSong> Search(IList<SongSearchSong> inList, string filter, Func<SongSearchSong, float> ordersort) {
			var words = filter.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
			var wordLengthInverses = words.Select(x => 1f / x.Length).ToArray();

			var possibleSongKey = 0u;

			if(words.Length == 1 && filter.Length >= 2 && filter.Length <= 7) {
				try {
					possibleSongKey = Convert.ToUInt32(filter, 16);
				} catch { }
			}

			// Slightly slower than just calling IsLetterOrDigit if its not a ' ', but in most of the cases it will be
			bool IsSpace(char x) => x == ' ' || !char.IsLetterOrDigit(x);

			var prefiltered = new List<xd>();

			var maxSearchWeight = 0f;
			var maxSortWeight = 0f;

			foreach(var x in inList) {
				int resultWeight = 0;
				bool matchedAuthor = false;
				int prevMatchIndex = -1;

				var songe = x.detailsSong;
				var songeName = songe.songName;

				if(possibleSongKey != 0 && x.detailsSong.mapId == possibleSongKey)
					resultWeight = 30;

				for(int i = 0; i < words.Length; i++) {
					if(!matchedAuthor && songe.songAuthorName.Equals(words[i], StringComparison.OrdinalIgnoreCase)) {
						matchedAuthor = true;

						resultWeight += 3 * (words[i].Length / 2);
						continue;
					} else if(!matchedAuthor && words[i].Length >= 3) {
						var index = songe.songAuthorName.IndexOf(words[i], StringComparison.OrdinalIgnoreCase);

						if(index == 0 || index > 0 && IsSpace(songe.songAuthorName[index - 1])) {
							matchedAuthor = true;

							resultWeight += (int)Math.Round((index == 0 ? 4 : 3) * (wordLengthInverses[i] * songe.songAuthorName.Length));
							continue;
						}
					}

					// Match the current split word in the song name
					var matchpos = songeName.IndexOf(words[i], StringComparison.OrdinalIgnoreCase);

					// If we found anything...
					if(matchpos != -1) {
						// Check if we matched the beginning of a word
						var wordStart = matchpos == 0 || IsSpace(songeName[matchpos - 1]);

						// If it was the beginning add 5 weighting, else 3
						resultWeight += wordStart ? 5 : 3;

						// If we did match the beginning, check if we matched an entire word. Get the end index as indicated by our needle
						var maybeWordEnd = wordStart && matchpos + words[i].Length < songeName.Length;

						// Check if we actually end up at a non word char, if so add 2 weighting
						if(maybeWordEnd && IsSpace(songeName[matchpos + words[i].Length]))
							resultWeight += 2;

						// If the word we just checked is behind the previous matched, add another 1 weight
						if(prevMatchIndex != -1 && matchpos > prevMatchIndex)
							resultWeight += 1;

						prevMatchIndex = matchpos;
					}
				}

				for(int i = 0; i < words.Length; i++) {
					if(words[i].Length > 3 && songe.levelAuthorName.IndexOf(words[i], StringComparison.OrdinalIgnoreCase) != -1) {
						resultWeight += 1;

						break;
					}
				}

				if(resultWeight > 0) {
					var sortWeight = ordersort(x);

					prefiltered.Add(new xd() {
						song = x,
						searchWeight = resultWeight,
						sortWeight = sortWeight
					});

					if(maxSearchWeight < resultWeight)
						maxSearchWeight = resultWeight;

					if(maxSortWeight < sortWeight)
						maxSortWeight = sortWeight;
				}
			}

			if(!prefiltered.Any())
				return new List<SongSearchSong>();

			var maxSearchWeightInverse = 1f / maxSearchWeight;
			var maxSortWeightInverse = 1f / maxSortWeight;

			return prefiltered.OrderByDescending((s) => {
				var searchWeight = s.searchWeight * maxSearchWeightInverse;

				return searchWeight + Math.Min(searchWeight / 2, s.sortWeight * maxSortWeightInverse * (searchWeight / 2));
			}).Select(x => x.song);
		}
	}
}
