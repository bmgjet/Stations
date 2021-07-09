using System.Reflection;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Stations", "bmgjet", "1.0.2")]
    class Stations : RustPlugin
    {
        #region Declarations
        const string perm = "Stations.use";
        const string permyoutube = "Stations.youtube";
        const string URL = "example.com:1234"; //Your YouTube2MP3 Server IP and Port
        bool BypassMp3Check = false;
        private PropertyInfo _CurrentRadioIp = typeof(BoomBox).GetProperty("CurrentRadioIp");
        #endregion

        #region Hooks
        void Init()
        {
            permission.RegisterPermission(perm, this);
            permission.RegisterPermission(permyoutube, this);
        }
        #endregion

        public void noBB(BasePlayer player)
        {
            player.ChatMessage("<color=red>Couldn't find boombox deployed or held.</color>");
        }

        public bool CheckLink(string url)
        {
            if (!BypassMp3Check)
            {
                if (!url.Contains(".mp3"))
                {
                    return false;
                }
            }
            if (!url.Contains("http"))
            {
                return false;
            }
            return true;
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
            player.ChatMessage("Please wait for music to download/encode!");
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
            player.ChatMessage("Please wait for music to download/encode!");
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

        [ChatCommand("youtube")]
        void ItemChangeYoutube(BasePlayer player, string arg2, string[] args)
        {
            if (!player.IPlayer.HasPermission(permyoutube))
            {
                player.ChatMessage("<color=red>Dont have persmission to use this command!</color>");
                return;
            }
            if (URL == "example.com:1234")
            {
                player.ChatMessage("<color=red>Plugin Youtube2MP3 server URL must be setup</color>");
                return;
            }
            //check there is a url arg
            if (args.Length != 1)
            {
                player.ChatMessage("InvalidArgs");
                return;
            }
            if (!args[0].Contains("youtube"))
            {
                player.ChatMessage("<color=red>Must be a youtube link!</color>");
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
                ChangeStationPortable(portableradio, "http://" + URL + "/RUST:" + player.displayName + "?YT:" + args[0], player);
                return;
            }
            //check for deployed boombox
            DeployableBoomBox radio = FindBox(player) as DeployableBoomBox;
            if (radio == null)
            {
                return;
            }
            ChangeStationDeployed(radio, "http://" + URL + "/RUST:" + player.displayName + "?YT:" + args[0], player);
            return;
        }


        [ChatCommand("station")]
        void ItemChangeStation(BasePlayer player, string arg2, string[] args)
        {
            if (!player.IPlayer.HasPermission(perm))
            {
                player.ChatMessage("<color=red>Dont have persmission to use this command!</color>");
                return;
            }
            //check there is a url arg
            if (args.Length != 1)
            {
                player.ChatMessage("InvalidArgs");
                return;
            }
            //checks its a valid stream
            if (!CheckLink(args[0]))
            {
                player.ChatMessage("<color=red>Must be a link to a mp3!</color>");
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