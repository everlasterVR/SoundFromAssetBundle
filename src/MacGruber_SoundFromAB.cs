using UnityEngine;
using System.Collections.Generic;
using Request = MeshVR.AssetLoader.AssetBundleFromFileRequest;
using MeshVR;
using MacGruber;

// LICENSE: CC BY-SA 4.0 https://creativecommons.org/licenses/by-sa/4.0/
// Based on the original SoundFromAB.cs by MacGruber from MacGruber.LogicBricks.14.var

namespace everlaster
{
	public class SoundFromAssetBundle : MVRScript
	{
		private JSONStorableUrl myAudioBundle;
		private JSONStorableBool myAcceptTrigger;
		private JSONStorableBool myAvoidRepetition;
		private JSONStorableStringChooser myAutoplayChooser;
		private JSONStorableStringChooser myModeChooser;

		private string myBundleURL = null;
		private Request myBundleRequest = null;
		private AudioSourceControl myAudioSourceControl;
		private AudioSource myAudioSource;

		private const string AUTOPLAY_OFF = "Off";
		private const string AUTOPLAY_NEXT = "Next";
		private const string AUTOPLAY_SHUFFLE = "Shuffle";
		private static string AUTOPLAY_MODE = AUTOPLAY_OFF;

		private int myModeIndex = DEFAULT_MODE;
		private static readonly List<string> MODE_NAMES = new List<string> { "PlayNext", "PlayNextClearQueue", "QueueClip", "PlayNow", "PlayIfClear", "PlayNowLoop", "PlayNowClearQueue" };
		private const int DEFAULT_MODE = 6;

		private Dictionary<string, Folder> myFolders = new Dictionary<string, Folder>();
		public static string lastPlayedFolderName;
		public static bool skipLastClipCheckOnce;

		private class Folder
		{
			public Folder(string name)
			{
				this.name = name;
			}

			public void ClearForRebuild()
			{
				soundIndex.Clear();
				sounds.Clear();
			}

			public void ClearShuffle()
			{
				shuffledIndices.Clear();
				currentShuffleIndex = -1;
			}

			public void AddSound(string displayName, NamedAudioClip clip)
			{
				int index = sounds.Count;
				sounds.Add(clip);
				soundIndex.Add(displayName, index);
			}

			public void Register(SoundFromAssetBundle self, string folderName)
			{
				this.self = self;
				List<string> displays = new List<string>();
				foreach (var sound in soundIndex)
					displays.Add(sound.Key);
				displays.Sort();

				if (chooser == null)
				{
					string singleActionName = string.IsNullOrEmpty(folderName) ? "PlaySpecific" : $"PlaySpecific [{folderName}]";
					string randomActionName = string.IsNullOrEmpty(folderName) ? "PlayRandom" : $"PlayRandom [{folderName}]";
					string prevActionName = string.IsNullOrEmpty(folderName) ? "PlayPrevious" : $"PlayPrevious [{folderName}]";
					string nextActionName = string.IsNullOrEmpty(folderName) ? "PlayNext" : $"PlayNext [{folderName}]";
					chooser = new JSONStorableStringChooser(singleActionName, displays, "", "");
					chooser.isStorable = chooser.isRestorable = false;
					singleAction = new JSONStorableActionStringChooser(singleActionName, PlaySingleSound, chooser);
					self.RegisterStringChooserAction(singleAction);

					randomAction = new JSONStorableAction(randomActionName, PlayRandomSound);
					prevAction = new JSONStorableAction(prevActionName, PlayPreviousSound);
					nextAction = new JSONStorableAction(nextActionName, PlayNextSound);
					self.RegisterAction(randomAction);
					self.RegisterAction(prevAction);
					self.RegisterAction(nextAction);
				}
				else
				{
					chooser.choices = displays;
					chooser.valNoCallback = string.Empty;
				}
			}

			public bool DeregisterIfEmpty()
			{
				if (self == null)
					return true;
				if (sounds.Count != 0)
					return false;

				self.DeregisterStringChooserAction(singleAction);
				chooser = null;
				singleAction = null;

				self.DeregisterAction(randomAction);
				self.DeregisterAction(prevAction);
				self.DeregisterAction(nextAction);
				randomAction = null;
				prevAction = null;
				nextAction = null;
				return true;
			}

			private void PlaySingleSound(string displayName)
			{
				if (self == null || !self.myAcceptTrigger.val)
					return;

				int idx;
				soundIndex.TryGetValue(displayName, out idx);
				PlayIndexClip(idx, true);
			}

			private void PlayRandomSound()
			{
				if (self == null || !self.myAcceptTrigger.val)
					return;

				int count = sounds.Count;
				if (count == 0)
					return;

				bool avoidRepetition = self.myAvoidRepetition.val;
				if (avoidRepetition)
				{
					if (count < 3 || myPreviousIndex1 >= count)
						myPreviousIndex1 = int.MaxValue;
					if (count < 4 || myPreviousIndex2 >= count)
						myPreviousIndex2 = int.MaxValue;
					if (myPreviousIndex1 == myPreviousIndex2)
						myPreviousIndex2 = int.MaxValue;
					if (myPreviousIndex1 < int.MaxValue)
						--count;
					if (myPreviousIndex2 < int.MaxValue)
						--count;
				}

				int idx = UnityEngine.Random.Range(0, count);

				if (avoidRepetition)
				{
					if (idx >= Mathf.Min(myPreviousIndex1, myPreviousIndex2))
						++idx;
					if (idx >= Mathf.Max(myPreviousIndex1, myPreviousIndex2))
						++idx;
				}

				PlayIndexClip(idx, true);
			}

			private void PlayPreviousSound()
			{
				if (self == null || !self.myAcceptTrigger.val)
					return;

				if(AUTOPLAY_MODE == AUTOPLAY_SHUFFLE)
				{
					if(currentShuffleIndex > 0) currentShuffleIndex--;
					if(currentShuffleIndex >= 0 && currentShuffleIndex < shuffledIndices.Count)
					{
						PlayIndexClip(shuffledIndices[currentShuffleIndex]);
					}
					else
					{
						PlayIndexClip(currentIdx);
					}
				}
				else
				{
					PlayIndexClip(currentIdx == 0 ? currentIdx : currentIdx - 1);
				}
			}

			private void PlayNextSound()
			{
				if (self == null || !self.myAcceptTrigger.val)
					return;

				if(AUTOPLAY_MODE == AUTOPLAY_SHUFFLE)
				{
					if(currentShuffleIndex < shuffledIndices.Count - 1)
					{
						currentShuffleIndex++;
						if(currentShuffleIndex >= 0 && currentShuffleIndex < shuffledIndices.Count)
						{
							PlayIndexClip(shuffledIndices[currentShuffleIndex]);
						}
						else
						{
							PlayRandomSound();
						}
					}
					else
					{
						PlayRandomSound();
					}
				}
				else
				{
					PlayIndexClip(currentIdx + 1);
				}
			}

			private void PlayIndexClip(int idx, bool addToShuffleList = false)
			{
				if (idx < 0 || idx >= sounds.Count)
					return;

				if(addToShuffleList && AUTOPLAY_MODE == AUTOPLAY_SHUFFLE)
				{
					shuffledIndices.Add(idx);
					currentShuffleIndex = shuffledIndices.Count - 1;
				}

				myPreviousIndex2 = myPreviousIndex1;
				myPreviousIndex1 = idx;
				currentIdx = idx;

				NamedAudioClip clip = sounds[idx];
				if (self == null || clip == null)
					return;
				AudioSourceControl asc = self.myAudioSourceControl;
				if (asc == null)
					return;

				switch (self.myModeIndex)
				{
					case 0: asc.PlayNext(clip);           break;
					case 1: asc.PlayNextClearQueue(clip); break;
					case 2: asc.QueueClip(clip);          break;
					case 3: asc.PlayNow(clip);            break;
					case 4: asc.PlayIfClear(clip);        break;
					case 5: asc.PlayNowLoop(clip);        break;
					case 6: asc.PlayNowClearQueue(clip);  break;
					default: break;
				}

				SoundFromAssetBundle.lastPlayedFolderName = name;
				SoundFromAssetBundle.skipLastClipCheckOnce = true;
			}

			private string name;
			private JSONStorableStringChooser chooser;
			private JSONStorableActionStringChooser singleAction;
			private JSONStorableAction randomAction;
			private JSONStorableAction prevAction;
			private JSONStorableAction nextAction;
			private Dictionary<string, int> soundIndex = new Dictionary<string, int>();
			private List<NamedAudioClip> sounds = new List<NamedAudioClip>();
			private int currentIdx = -1;
			private int myPreviousIndex1 = int.MaxValue;
			private int myPreviousIndex2 = int.MaxValue;
			private readonly List<int> shuffledIndices = new List<int>();
			int currentShuffleIndex = -1;
			private SoundFromAssetBundle self;
		}

		public override void Init()
		{
			Utils.OnInitUI(CreateUIElement);

			string storableName = string.Empty;
			if (containingAtom.type == "Person")
				storableName = "HeadAudioSource";
			else if (containingAtom.type == "AudioSource")
				storableName = "AudioSource";
			else if (containingAtom.type == "AptSpeaker")
				storableName = "AptSpeaker_Import";
			else if (containingAtom.type == "RhythmAudioSource")
				storableName = "RhythmSource";
			myAudioSourceControl = containingAtom.GetStorableByID(storableName) as AudioSourceControl;

			if (myAudioSourceControl == null)
			{
				SuperController.LogError("The SoundFromAB plugin needs to be placed on a Person, AudioSource, RhythmAudioSource or AptSpeaker atom.");
				Utils.SetupInfoTextNoScroll(this,
					"<color=#FF6060><size=40><b>SoundFromAB</b></size>\nThis plugin needs to be placed on a Person, AudioSource, RhythmAudioSource or AptSpeaker atom.</color>",
					500.0f, true
				);
				return;
			}

			myAudioSource = myAudioSourceControl.audioSource;

			Utils.SetupInfoTextNoScroll(this,
				$"<color=#606060><size=40><b>{nameof(SoundFromAssetBundle)}</b></size>" +
				"\n<size=24><i>Based on SoundFromAB by MacGruber</i></size>" +
				"\n\nExposes audio files found in a <i>Unity AssetBundle</i> folders as <i>Triggers</i>:" +
				"\n\n- <b><i>PlaySpecific</i></b> plays the selected clip" +
				"\n- <b><i>PlayRandom</i></b> picks a random clip" +
				"\n- <b><i>PlayPrevious</i></b> plays the previous clip" +
				"\n- <b><i>PlayNext</i></b> plays the next clip" +
				"\n\n<b>Autoplay</b> will automatically play the next clip or a random clip (shuffle) when a clip ends. If playing the next clip, autoplay will stop on the last clip in the folder, while autoplaying a random clip will play indefinitely." +
				" Shuffle will also cause the PlayNext action to trigger PlayRandom instead, and the PlayPrevious action to play the previously shuffled clip." +
				"\n\nIt's recommended to use file extension <i>*.assetbundle</i> for your <i>AssetBundle</i> files. While other extensions are supported, VaM will not auto-detect those as dependencies when creating a VAR." +
				"\n\n<b>Avoid Repetition</b> discards the last 1-2 choices (depending on the number of total available choices) when using PlayRandom or Autoplay Shuffle." +
				"\n\n<b>Mode</b> controls how audio should be played. These correspond to the modes in the AudioSource receiver, and can be set by trigger just before calling one of the above trigger actions.</color>",
				1200.0f, true
			);

			myAudioBundle = Utils.SetupAssetBundleChooser(this, "AssetBundle", "", false, "assetbundle|audiobundle|voicebundle");
			myAudioBundle.setCallbackFunction += (string url) => {
				if (myBundleURL != null)
					AssetLoader.DoneWithAssetBundleFromFile(myBundleURL);
				myBundleURL = url;

				//SuperController.LogMessage(myBundleURL);
				if (string.IsNullOrEmpty(myBundleURL))
				{
					OnAssetBundleLoaded(null);
				}
				else
				{
					try {
						Request request = new AssetLoader.AssetBundleFromFileRequest {path = myBundleURL, callback = OnAssetBundleLoaded};
						AssetLoader.QueueLoadAssetBundleFromFile(request);
					}
					catch (System.Exception e)
					{ SuperController.LogError(e.ToString()); }
				}
			};

			myAcceptTrigger = Utils.SetupToggle(this, "AcceptTrigger", true, false);
			myAvoidRepetition = Utils.SetupToggle(this, "Avoid Repetition", true, false);
			myAutoplayChooser = Utils.SetupStringChooser(this, "Autoplay", new List<string> { AUTOPLAY_OFF, AUTOPLAY_NEXT, AUTOPLAY_SHUFFLE }, 0, false);
			myAutoplayChooser.setCallbackFunction += (string v) =>
			{
				AUTOPLAY_MODE = v;
				if(AUTOPLAY_MODE == AUTOPLAY_SHUFFLE)
				{
					foreach (var folder in myFolders)
					{
						folder.Value.ClearShuffle();
					}
				}
			};
			myModeChooser = Utils.SetupStringChooser(this, "Mode", MODE_NAMES, DEFAULT_MODE, false);
			myModeChooser.setCallbackFunction += (string v) => {
				myModeIndex = MODE_NAMES.FindIndex((string entry) => entry == v);
				if (myModeIndex < 0)
				{
					myModeIndex = DEFAULT_MODE;
					myModeChooser.valNoCallback = MODE_NAMES[myModeIndex];
				}
			};

			Utils.SetupAction(this, "StopLoop", StopLoop);
			Utils.SetupAction(this, "Stop", Stop);
			Utils.SetupAction(this, "StopAndClearQueue", StopAndClearQueue);
			Utils.SetupAction(this, "ClearQueue", ClearQueue);
		}

		private void OnAssetBundleLoaded(Request aRequest)
		{
			try {
				myBundleRequest = aRequest;

				foreach (var folder in myFolders)
				{
					folder.Value.ClearForRebuild();
				}

				if (myBundleRequest != null)
				{
					string[] assets = myBundleRequest.assetBundle?.GetAllAssetNames();
					for (int i=0; i<assets.Length; ++i)
					{
						string uid = assets[i];
						if (!uid.EndsWith(".wav") && !uid.EndsWith(".ogg") && !uid.EndsWith(".mp3") && !uid.EndsWith(".aif"))
							continue;

						int start = uid.StartsWith("assets/") ? 7 : 0;
						int end = uid.LastIndexOf('/');

						string folderName;
						string displayName;
						if (end >= 0)
						{
							folderName = uid.Substring(start, end-start);
							displayName = uid.Substring(end+1);
						}
						else
						{
							folderName = string.Empty;
							displayName = uid.Substring(start);
						}

						// add extra space to fool VaM's dependency detection *facepalm*
						displayName += " ";

						AssetBundleAudioClip clip = new AssetBundleAudioClip(myBundleRequest, "", uid);
						if (clip == null)
							continue;

						Folder folder;
						if (!myFolders.TryGetValue(folderName, out folder))
						{
							folder = new Folder(folderName);
							myFolders.Add(folderName, folder);
						}
						folder.AddSound(displayName, clip);
					}

					foreach (var folder in myFolders)
					{
						folder.Value.Register(this, folder.Key);
					}
				}

				List<string> toRemove = new List<string>();
				foreach (var folder in myFolders)
				{
					if (folder.Value.DeregisterIfEmpty())
						toRemove.Add(folder.Key);
				}
				for (int i=0; i<toRemove.Count; ++i)
				{
					myFolders.Remove(toRemove[i]);
				}
			}
			catch (System.Exception e)
			{ SuperController.LogError(e.ToString()); }
		}

		private void StopLoop()
		{
			if (myAudioSourceControl == null) return;
			myAudioSourceControl.StopLoop();
		}

		private void Stop()
		{
			if (myAudioSourceControl == null) return;
			// if (myAudioSource != null) myAudioSource.time = 0;
			myAudioSourceControl.Stop();
			prevClip = null; // prevent autoplay next
		}

		private void StopAndClearQueue()
		{
			if (myAudioSourceControl == null) return;
			ClearQueue();
			Stop();
		}

		private void ClearQueue()
		{
			if (myAudioSourceControl == null) return;
			myAudioSourceControl.ClearQueue();
		}

		private AudioClip prevClip;

		private void Update()
		{
			if (myBundleURL == null)
				myAudioBundle.setCallbackFunction(myAudioBundle.val);

			if (myAutoplayChooser.val == AUTOPLAY_OFF || myAudioSource == null || lastPlayedFolderName == null)
				return;

			var clip = myAudioSource.clip;
			if(skipLastClipCheckOnce)
			{
				skipLastClipCheckOnce = false;
			}
			else if (this.enabled && prevClip != null && prevClip != clip)
			{
				if (myFolders.ContainsKey(lastPlayedFolderName))
				{
					this.CallAction($"PlayNext [{lastPlayedFolderName}]");
					skipLastClipCheckOnce = true;
				}
			}

			prevClip = clip;
		}

		private void OnApplicationPause(bool pauseStatus)
		{
			if(pauseStatus)
			{
				skipLastClipCheckOnce = true;
			}
		}

		private void OnDestroy()
		{
			if (myBundleURL != null)
				AssetLoader.DoneWithAssetBundleFromFile(myBundleURL);
			myBundleURL = null;
		}
	}
}
