using System.Reflection;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Stations", "bmgjet", "1.0.1")]
    class Stations : RustPlugin
    {
        #region Declarations
        const string perm = "Stations.use";
        private PropertyInfo _CurrentRadioIp = typeof(BoomBox).GetProperty("CurrentRadioIp");
        #endregion

        #region Hooks
        void Init()
        {
            permission.RegisterPermission(perm, this);
        }
        #endregion

        public void noBB(BasePlayer player)
        {
            player.ChatMessage("<color=red>Could'nt find boombox deployed or held.</color>");
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
            if (!args[0].Contains(".mp3") || !args[0].Contains("http"))
            {
                player.ChatMessage("<color=red>Must be a link to a mp3!</color>");
                return;
            }
            try
            {
                if(player.IsHoldingEntity<HeldBoomBox>())
                {
                    //Must be boombox portable
                    HeldBoomBox portableradio = player.GetActiveItem().GetHeldEntity() as HeldBoomBox;
                    if (portableradio == null)
                    {
                        noBB(player);
                        return;
                    }

                    if (args.Length == 1)
                    {
                        //send link to MP3 Stream provided.
                        _CurrentRadioIp.SetValue(portableradio.BoxController, args[0]);
                        //portableradio.BoxController.CurrentRadioIp = args[1]; 
                    }
                    //siwtch off old channel
                    portableradio.BoxController.ServerTogglePlay(false);
                    //use current selected mp3 stream.
                    portableradio.BoxController.baseEntity.ClientRPC<string>(null, "OnRadioIPChanged", portableradio.BoxController.CurrentRadioIp);
                    //start playing
                    portableradio.BoxController.ServerTogglePlay(true);
                    //End of portable boombox
                    return;
                }


                    //check for deployed boombox
                    RaycastHit rhit;
                    if (!Physics.Raycast(player.eyes.HeadRay(), out rhit))
                    {
                    noBB(player);
                    return;
                    }
                    var heldEntity = rhit.GetEntity();
                    if (heldEntity == null)
                    {
                    noBB(player);
                    return;
                    }
                    if (rhit.distance > 5f)
                    {
                    noBB(player);
                    return;
                    }
                    if (heldEntity.ShortPrefabName.Contains("boombox"))
                    {
                        DeployableBoomBox radio = heldEntity as DeployableBoomBox;
                        if (radio == null) return;
                        //send link to MP3 Stream provided.
                        _CurrentRadioIp.SetValue(radio.BoxController, args[0]);
                        //switch off old channel
                        radio.BoxController.ServerTogglePlay(false);
                        //use current selected mp3 stream.
                        radio.BoxController.baseEntity.ClientRPC<string>(null, "OnRadioIPChanged", radio.BoxController.CurrentRadioIp);
                        //play
                        radio.BoxController.ServerTogglePlay(true);
                        return;
                    }
            }
            catch {}
            noBB(player);
        }

    }
}