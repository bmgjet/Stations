using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Stations", "bmgjet", "1.0.2")]
    class Stations : RustPlugin
    {
        #region Declarations
        const string perm = "Stations.use";
        const string permYT = "Stations.youtube";  //Used to send requests to stationsserver.exe
        const string permAdmin = "Stations.admin"; //Used to change settings from within game.

        private static SaveData _data;
        private static PluginConfig config;
        private PropertyInfo _CurrentRadioIp = typeof(BoomBox).GetProperty("CurrentRadioIp");
        #endregion

        #region Configuration
        private class PluginConfig
        {
            [JsonProperty(PropertyName = "Stations Server URL: ")] public string URL { get; set; }
            [JsonProperty(PropertyName = "Cool Down Seconds: ")] public float Cooldown { get; set; }
            [JsonProperty(PropertyName = "Bypass MP3 Check: ")] public bool MP3Check { get; set; }
        }

        private PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                URL = "example.com:1234",
                Cooldown = 10f,
                MP3Check = true,
            };
        }

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            Config.WriteObject(GetDefaultConfig(), true);
            config = Config.ReadObject<PluginConfig>();
        }
        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }
        private void WriteSaveData() =>
        Interface.Oxide.DataFileSystem.WriteObject(Name, _data);

        class SaveData
        {
            public Dictionary<string, DateTime> CoolDown = new Dictionary<string, DateTime>();
        }
        #endregion

        #region Hooks
        void Init()
        {
            permission.RegisterPermission(perm, this);
            permission.RegisterPermission(permYT, this);
            permission.RegisterPermission(permAdmin, this);

            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(Name))
                Interface.Oxide.DataFileSystem.GetDatafile(Name).Save();

            _data = Interface.Oxide.DataFileSystem.ReadObject<SaveData>(Name);
            if (_data == null)
            {
                WriteSaveData();
            }

            config = Config.ReadObject<PluginConfig>();
            if (config == null)
            {
                LoadDefaultConfig();
            }
        }
        void Unload()
        {
            if (_data != null)
                _data = null;

            if (config != null)
                config = null;
        }
        #endregion

        public void noBB(BasePlayer player)
        {
            player.ChatMessage("<color=red>Couldn't find boombox deployed or held.</color>");
        }

        public bool CheckLink(string url, BasePlayer player)
        {
            if (Cooldown(player).TotalSeconds > 0)
            {
                player.ChatMessage("Please wait for cooldown " + Cooldown(player).ToString("g"));
                return false;
            }
            if (config.MP3Check)
            {
                if (!url.ToLower().Contains(".mp3"))
                {
                    player.ChatMessage("<color=red>Must be a mp3!</color>");
                    return false;
                }
            }
            if (!url.ToLower().Contains("http"))
            {
                player.ChatMessage("<color=red>Must be a link!</color>");
                return false;
            }
            if (url.ToLower().Contains("youtube.com") || url.ToLower().Contains("youtu.be"))
            {
                player.ChatMessage("<color=red>Not supported use /yt link</color>");
                return false;
            }
            return true;
        }
        private TimeSpan CeilingTimeSpan(TimeSpan timeSpan) =>
        new TimeSpan((long)Math.Ceiling(1.0 * timeSpan.Ticks / 10000000) * 10000000);

        public TimeSpan Cooldown(BasePlayer player)
        {
            if (_data.CoolDown.ContainsKey(player.UserIDString))
            {
                DateTime lastPlayed = _data.CoolDown[player.UserIDString];
                return CeilingTimeSpan(lastPlayed.AddSeconds(config.Cooldown) - DateTime.Now);
            }
            _data.CoolDown.Add(player.UserIDString, DateTime.Now);
            WriteSaveData();
            return DateTime.Now - DateTime.Now;
        }

        public void ChangeStationPortable(HeldBoomBox portableradio, string newlink, BasePlayer player)
        {
            //send link to MP3 Stream provided.
            _CurrentRadioIp.SetValue(portableradio.BoxController, newlink);
            //siwtch off old channel
            portableradio.BoxController.ServerTogglePlay(false);
            //use current selected mp3 stream.
            portableradio.BoxController.baseEntity.ClientRPC<string>(null, "OnRadioIPChanged", portableradio.BoxController.CurrentRadioIp);
            //start playing
            portableradio.BoxController.ServerTogglePlay(true);
            Puts(player.displayName + " Played: " + newlink);
            if (_data.CoolDown.ContainsKey(player.UserIDString))
            {
                _data.CoolDown[player.UserIDString] = DateTime.Now;
                WriteSaveData();
            }
        }

        public void ChangeStationDeployed(DeployableBoomBox radio, string newlink, BasePlayer player)
        {
            //send link to MP3 Stream provided.
            _CurrentRadioIp.SetValue(radio.BoxController, newlink);
            //switch off old channel
            radio.BoxController.ServerTogglePlay(false);
            //use current selected mp3 stream.
            radio.BoxController.baseEntity.ClientRPC<string>(null, "OnRadioIPChanged", radio.BoxController.CurrentRadioIp);
            //play
            radio.BoxController.ServerTogglePlay(true);
            Puts(player.displayName + " Played: " + newlink);
            if (_data.CoolDown.ContainsKey(player.UserIDString))
            {
                _data.CoolDown[player.UserIDString] = DateTime.Now;
                WriteSaveData();
            }
        }

        public void CheckSettings(string[] args, BasePlayer player)
        {
            if (args.Length == 2)
            {
                switch (args[0])
                {
                    case "mp3":
                        config.MP3Check = !config.MP3Check;
                        SaveConfig();
                        player.ChatMessage("Must be MP3: " + config.MP3Check);
                        return;
                    case "url":
                        config.URL = args[1];
                        SaveConfig();
                        return;
                    case "cooldown":
                        try
                        {
                            config.Cooldown = float.Parse(args[1]);
                        }
                        catch { config.Cooldown = 10f; }
                        SaveConfig();
                        return;
                }
            }
        }

        public void Status(string DLURL, BasePlayer player, string oldmsg)
        {
            //shows in chat stations servers download/conversion status.
            timer.Once(0.5f, () =>
            {
                webrequest.Enqueue(DLURL, null, (code, response) =>
                {
                    if (code != 200 || response == null)
                    {
                        Puts($"Couldn't get an response");
                        return;
                    }
                    if (response.Contains("Ready To Play") || response.Contains("No File") || response.Contains("Aborting."))
                    {
                        player.ChatMessage(response);
                        return;
                    }
                    if (response != oldmsg)
                    {
                        if(response == "Converting")
                        {
                            player.ChatMessage("Downloaded 100%");
                        }
                        player.ChatMessage(response);
                    }
                    Status(DLURL, player, response);
                }, this, RequestMethod.GET);
            });
        }

        public BaseEntity FindBox(BasePlayer player)
        {
            RaycastHit rhit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out rhit))
            {
                noBB(player);
                return null;
            }
            var heldEntity = rhit.GetEntity();
            if (heldEntity == null)
            {
                noBB(player);
                return null;
            }
            if (rhit.distance > 5f)
            {
                noBB(player);
                return null;
            }
            if (heldEntity.ShortPrefabName.Contains("boombox"))
            {
                return heldEntity;
            }
            noBB(player);
            return null;
        }

        [ChatCommand("YT")]
        void ChangeYoutube(BasePlayer player, string arg2, string[] args)
        {
            if (!player.IPlayer.HasPermission(permYT))
            {
                player.ChatMessage("<color=red>Dont have persmission to use this command!</color>");
                return;
            }
            if (config.URL == "example.com:1234")
            {
                player.ChatMessage("<color=red>Plugin Station Server URL must be setup</color>");
                return;
            }

            if (args.Length != 1)
            {
                player.ChatMessage("InvalidArgs");
                return;
            }
            if (Cooldown(player).TotalSeconds > 0)
            {
                player.ChatMessage("Please wait for cooldown " + Cooldown(player).ToString("g"));
                return;
            }
            if (!args[0].ToLower().Contains("youtube") && !args[0].ToLower().Contains("youtu.be"))
            {
                player.ChatMessage("<color=red>Must be a youtube link!</color>");
                return;
            }

            string DLURL = "http://" + config.URL + "/RUST:" + player.UserIDString + "?YT:" + args[0];

            if (player.IsHoldingEntity<HeldBoomBox>())
            {
                //Must be boombox portable
                HeldBoomBox portableradio = player.GetActiveItem().GetHeldEntity() as HeldBoomBox;
                if (portableradio == null)
                {
                    noBB(player);
                    return;
                }
                ChangeStationPortable(portableradio, DLURL, player);
                Status(DLURL.Replace("?YT:", "?DL:"), player, "NULL");
                return;
            }
            //check for deployed boombox
            DeployableBoomBox radio = FindBox(player) as DeployableBoomBox;
            if (radio == null)
            {
                return;
            }
            ChangeStationDeployed(radio, DLURL, player);
            Status(DLURL.Replace("?YT:", "?DL:"), player, "NULL");
            return;
        }


        [ChatCommand("station")]
        void ChangeStation(BasePlayer player, string arg2, string[] args)
        {
            if (!player.IPlayer.HasPermission(perm))
            {
                player.ChatMessage("<color=red>Dont have persmission to use this command!</color>");
                return;
            }
            //check there is a url arg
            if (args.Length != 1)
            {
                if (player.IPlayer.HasPermission(permAdmin))
                {
                    CheckSettings(args, player);
                    return;
                }
                player.ChatMessage("InvalidArgs");
                return;
            }
            //checks its a valid stream
            if (!CheckLink(args[0], player))
            {
                return;
            }
            if (player.IsHoldingEntity<HeldBoomBox>())
            {
                //Must be boombox portable
                HeldBoomBox portableradio = player.GetActiveItem().GetHeldEntity() as HeldBoomBox;
                if (portableradio == null)
                {
                    noBB(player);
                    return;
                }
                ChangeStationPortable(portableradio, args[0], player);
                return;
            }
            //check for deployed boombox
            DeployableBoomBox radio = FindBox(player) as DeployableBoomBox;
            if (radio == null) return;
            ChangeStationDeployed(radio, args[0], player);
        }
    }
}