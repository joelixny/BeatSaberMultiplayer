﻿using BeatSaberMultiplayerServer.Misc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BeatSaberMultiplayerServer
{
    [Serializable]
    class PlayerInfo
    {
        public string playerName;
        public ulong playerId;

        public int playerScore;
        public int playerCutBlocks;
        public int playerComboBlocks;
        public int playerEnergy;

        public string playerAvatar;
        

        public PlayerInfo(string _name, ulong _id, string _avatar = null)
        {
            playerName = _name;
            playerId = _id;
            playerAvatar = _avatar;
        }
    }

    enum ServerState { Voting, Preparing, Playing };

    enum ServerCommandType {SetServerState, SetLobbyTimer, DownloadSongs, StartSelectedSongLevel, SetPlayerInfos, SetSelectedSong, UpdateRequired, Ping, Kicked };

    class ServerCommand
    {
        public string version = "0.4.5.0";
        public ServerCommandType commandType;
        public ServerState serverState;
        public int lobbyTimer;
        public string[] songsToDownload;
        public string selectedLevelID;
        public int selectedSongDifficlty;
        public string[] playerInfos;
        public double selectedSongDuration;
        public double selectedSongPlayTime;
        public string kickReason;
        public bool noFailMode;
        public string scoreboardScoreFormat;

        public ServerCommand(ServerCommandType _type, int _timer = 0, string[] _songs = null, int _difficulty = 0, string[] _playerInfos = null, double _selectedSongDuration = 0, double _selectedSongPlayTime = 0, string _kickReason = null)
        {
            version = Assembly.GetEntryAssembly().GetName().Version.ToString();
            commandType = _type;
            lobbyTimer = _timer;
            songsToDownload = _songs;
            serverState = ServerMain.serverState;
            selectedLevelID = (ServerMain.currentSongIndex >= 0 && ServerMain.availableSongs.Count > ServerMain.currentSongIndex) ? ServerMain.availableSongs[ServerMain.currentSongIndex].levelId : "";
            selectedSongDifficlty = _difficulty;
            playerInfos = _playerInfos;
            selectedSongDuration = _selectedSongDuration;
            selectedSongPlayTime = _selectedSongPlayTime;
            kickReason = _kickReason;
            noFailMode = Settings.Instance.Server.NoFailMode;
            scoreboardScoreFormat = Settings.Instance.Server.ScoreboardScoreFormat;
        }
    }

    enum ClientCommandType { GetServerState, SetPlayerInfo, GetAvailableSongs, VoteForSong };

    [Serializable]
    class ClientCommand
    {
        public string version = "0.4.5.0";
        public ClientCommandType commandType;
        public string playerInfo;
        public string voteForLevelId;

        public ClientCommand(ClientCommandType _type, string _playerInfo = null, string _voteForLevelId = null)
        {
            commandType = _type;
            playerInfo = _playerInfo;
            voteForLevelId = _voteForLevelId;
        }

    }

    public enum ClientState { Disconnected, Connected, Playing, UpdateRequired};

    public enum Difficulty { Easy, Normal, Hard, Expert, ExpertPlus };

    [Serializable]
    public class CustomSongInfo
    {
        public string songName;
        public string songSubName;
        public string authorName;
        public float beatsPerMinute;
        public float previewStartTime;
        public float previewDuration;
        public string environmentName;
        public string coverImagePath;
        public string videoPath;
        public DifficultyLevel[] difficultyLevels;
        public string path;
        public string levelId;

        public string beatSaverId;
        public TimeSpan duration;

        [Serializable]
        public class DifficultyLevel
        {
            public string difficulty;
            public int difficultyRank;
            public string audioPath;
            public string jsonPath;
            public string json;
        }

        public string GetIdentifier()
        {
            var combinedJson = "";
            foreach (var diffLevel in difficultyLevels)
            {
                if (!File.Exists(path + "/" + diffLevel.jsonPath))
                {
                    continue;
                }

                diffLevel.json = File.ReadAllText(path + "/" + diffLevel.jsonPath);
                combinedJson += diffLevel.json;
            }

            var hash = MD5Hash(combinedJson);
            levelId = hash + "∎" + string.Join("∎", new[] { songName, songSubName, authorName, beatsPerMinute.ToString() }) + "∎";
            return levelId;
        }

        public static string MD5Hash(string input)
        {
            StringBuilder hash = new StringBuilder();
            MD5CryptoServiceProvider md5provider = new MD5CryptoServiceProvider();
            byte[] bytes = md5provider.ComputeHash(new UTF8Encoding().GetBytes(input));

            for (int i = 0; i < bytes.Length; i++)
            {
                hash.Append(bytes[i].ToString("x2"));
            }
            return hash.ToString().ToUpper();
        }
    }
}
