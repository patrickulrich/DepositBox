using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using System.Globalization;

namespace Oxide.Plugins
{
    [Info("DepositBox", "saulteafarmer", "0.1.3")]
    [Description("Drop box that registers drops for admin while removing items from the game.")]
    internal class DepositBox : RustPlugin
    {
        // Configuration, Logging, and Tracking Classes
        private int DepositItemID;
        private ulong DepositBoxSkinID;
        private DepositLogger logger;
        private DepositTracker tracker;

        // Permission constant
        private const string permPlace = "depositbox.place";

        #region Oxide Hooks

        void Init()
        {
            logger = new DepositLogger();
            tracker = new DepositTracker();

            LoadConfiguration();
            permission.RegisterPermission(permPlace, this);
        }

        protected override void LoadDefaultConfig()
        {
            Config["DepositItemID"] = -1779183908;    // Default Item ID for deposits (paper)
            Config["DepositBoxSkinID"] = 1641384897;  // Default skin ID for the deposit box
            SaveConfig();
        }

        void OnEntitySpawned(StorageContainer container)
        {
            if (container == null || container.skinID != DepositBoxSkinID) return;  // Early return for non-matching containers

            if (!container.TryGetComponent(out DepositBoxRestriction mono))
            {
                mono = container.gameObject.AddComponent<DepositBoxRestriction>();
                mono.container = container.inventory;
                mono.InitDepositBox(this);
            }
        }

        #endregion

        #region Commands

        [ChatCommand("depositbox")]
        private void GiveDepositBox(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, permPlace))
            {
                player.ChatMessage(lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }

            player.inventory.containerMain.GiveItem(ItemManager.CreateByItemID(833533164, 1, DepositBoxSkinID));
            player.ChatMessage(lang.GetMessage("BoxGiven", this, player.UserIDString));
        }

        #endregion

        #region DepositBoxRestriction Class

        public class DepositBoxRestriction : FacepunchBehaviour
        {
            public ItemContainer container;
            private DepositBox parent;

            public void InitDepositBox(DepositBox depositBox)
            {
                parent = depositBox;
                container.canAcceptItem += CanAcceptItem;
                container.onItemAddedRemoved += OnItemAddedRemoved;
            }

            private bool CanAcceptItem(Item item, int targetPos)
            {
                if (item == null || item.info == null || item.info.itemid != parent.DepositItemID)
                {
                    return false;
                }

                if (item.GetOwnerPlayer() is BasePlayer player)
                {
                    parent.TrackDeposit(item, player);
                }

                return true;
            }

            private void OnItemAddedRemoved(Item item, bool added)
            {
                if (!added || item.info.itemid != parent.DepositItemID) return;

                if (parent.GetTrackedPlayer(item) is BasePlayer player)
                {
                    parent.LogDeposit(player, item.amount);
                    parent.RemoveTrackedDeposit(item);
                    item.Remove();
                }
            }

            public void Destroy()
            {
                container.canAcceptItem -= CanAcceptItem;
                container.onItemAddedRemoved -= OnItemAddedRemoved;
                Destroy(this);
            }
        }

        #endregion

        #region Logging

        public void LogDeposit(BasePlayer player, int amount)
        {
            logger.LogDeposit(player, amount);

            player.ChatMessage(lang.GetMessage("DepositRecorded", this, player.UserIDString)
                .Replace("{amount}", amount.ToString(CultureInfo.InvariantCulture)));
        }

        public void TrackDeposit(Item item, BasePlayer player)
        {
            tracker.TrackDeposit(item, player);
        }

        public BasePlayer GetTrackedPlayer(Item item)
        {
            return tracker.GetTrackedPlayer(item);
        }

        public void RemoveTrackedDeposit(Item item)
        {
            tracker.RemoveTrackedDeposit(item);
        }

        #endregion

        #region Configuration

        private void LoadConfiguration()
        {
            DepositItemID = Convert.ToInt32(Config["DepositItemID"]);
            DepositBoxSkinID = Convert.ToUInt64(Config["DepositBoxSkinID"]);
        }

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You do not have permission to place this box.",
                ["BoxGiven"] = "You have received a Deposit Box.",
                ["DepositRecorded"] = "Your deposit of {amount} has been recorded."
            }, this);
        }

        #endregion
    }

    #region Helper Classes

    public class DepositLogger
    {
        private readonly DepositLog depositLog;

        public DepositLogger()
        {
            depositLog = Interface.Oxide.DataFileSystem.ReadObject<DepositLog>("DepositBoxLog") ?? new DepositLog();
        }

        public void LogDeposit(BasePlayer player, int amount)
        {
            var entry = new DepositEntry
            {
                SteamId = player.UserIDString,
                Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                AmountDeposited = amount
            };

            Interface.Oxide.LogInfo($"Logging deposit: SteamID={entry.SteamId}, Amount={entry.AmountDeposited}, Timestamp={entry.Timestamp}");

            depositLog.Deposits.Add(entry);
            SaveDepositLog();
        }

        private void SaveDepositLog()
        {
            Interface.Oxide.DataFileSystem.WriteObject("DepositBoxLog", depositLog);
        }
    }

    public class DepositTracker
    {
        private readonly Dictionary<Item, BasePlayer> depositTrack = new Dictionary<Item, BasePlayer>();

        public void TrackDeposit(Item item, BasePlayer player)
        {
            if (item != null && player != null)
            {
                depositTrack[item] = player;
            }
        }

        public BasePlayer GetTrackedPlayer(Item item)
        {
            depositTrack.TryGetValue(item, out BasePlayer player);
            return player;
        }

        public void RemoveTrackedDeposit(Item item)
        {
            depositTrack.Remove(item);
        }
    }

    public class DepositLog
    {
        [JsonProperty("deposits")]
        public List<DepositEntry> Deposits { get; set; } = new List<DepositEntry>();
    }

    public class DepositEntry
    {
        [JsonProperty("steamid")]
        public string SteamId { get; set; }

        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }

        [JsonProperty("amount_deposited")]
        public int AmountDeposited { get; set; }
    }

    #endregion
}
