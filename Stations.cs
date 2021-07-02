using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Stations", "bmgjet", "1.0.0")]
    class Stations : RustPlugin
    {
        #region Declarations
        const string perm = "Stations.use";
        #endregion

        #region Hooks
        void Init()
        {
            permission.RegisterPermission(perm, this);
        }
        #endregion

        [ChatCommand("station")]
        void ItemChangeStation(BasePlayer player, string arg2, string[] args)
        {
            if (!player.IPlayer.HasPermission(perm))
            {
                player.ChatMessage("<color=red>Dont have persmission to use this command!</color>");
                return;
            }

            if (args.Length < 1)
            {
                player.ChatMessage("InvalidArgs");
                return;
            }
            BaseEntity heldEntity = player.GetActiveItem().GetHeldEntity(); //checks if its portable one.
            if (heldEntity == null || !heldEntity.ShortPrefabName.Contains("boomboxportable"))
            {
                //not portable so check what player is looking at.
                RaycastHit rhit;
                if (!Physics.Raycast(player.eyes.HeadRay(), out rhit))
                {
                    return;
                }
                heldEntity = rhit.GetEntity();
                if (heldEntity == null)
                {
                    return;
                }
                if (rhit.distance > 5f)
                {
                    return;
                }
                if (heldEntity.ShortPrefabName.Contains("boombox"))
                {
                    DeployableBoomBox radio = heldEntity as DeployableBoomBox;
                    if (radio == null) return;
                    if (args.Length == 2)
                    {
                        //send link to MP3 Stream provided.
                        radio.BoxController.CurrentRadioIp = args[1];
                    }
                        //use current selected mp3 stream.
                        radio.BoxController.baseEntity.ClientRPC<string>(null, "OnRadioIPChanged", radio.BoxController.CurrentRadioIp);

                    switch (args[0])
                    {
                        case "1":
                            radio.SetFlag(DeployableBoomBox.Flag_HasPower, true);
                            radio.BoxController.ServerTogglePlay(true);
                            break;
                        default:
                            radio.SetFlag(DeployableBoomBox.Flag_HasPower, false);
                            radio.BoxController.ServerTogglePlay(false);
                            break;
                    }
                }
                return;
            }

            //Must be boombox portable
            HeldBoomBox portableradio = heldEntity as HeldBoomBox;
            if (portableradio == null) return;
                if (args.Length == 2)
                {
                //send link to MP3 Stream provided.
                portableradio.BoxController.CurrentRadioIp = args[1];
                }
            //use current selected mp3 stream.
            portableradio.BoxController.baseEntity.ClientRPC<string>(null, "OnRadioIPChanged", portableradio.BoxController.CurrentRadioIp);


                switch (args[0])
                {
                case "1":
                    portableradio.BoxController.ServerTogglePlay(true);
                    break;
                default:
                    portableradio.BoxController.ServerTogglePlay(false);
                    break;
                }
        }
    }
}