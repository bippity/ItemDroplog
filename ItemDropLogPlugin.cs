using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
namespace ItemDropLog
{
	[ApiVersion(1, 21)]
	public class ItemDropLogPlugin : TerrariaPlugin
	{
		private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "itemdroplog.json");
		private object _dropLocker;
		private ItemDrop[] _drops;
		private object _pendingLocker;
		private IList<ItemDrop> _playerDropsPending;
		private IList<Item> _ignoredItems;
		public override string Author
		{
			get
			{
				return "IcyTerraria";
			}
		}
		public override string Name
		{
			get
			{
				return "Item Drop Logger";
			}
		}
		public override string Description
		{
			get
			{
				return "Item Drop Logger Plugin";
			}
		}
		public override Version Version
		{
			get
			{
				return new Version(0, 7, 4);
			}
		}
		public string SavePath
		{
			get
			{
				return TShock.SavePath;
			}
		}
        public static IDbConnection DB;
		
		public ItemDropLogPlugin(Main game) : base(game)
		{
			_dropLocker = new object();
			_drops = new ItemDrop[Main.item.Length];
			_pendingLocker = new object();
			_playerDropsPending = new List<ItemDrop>(Main.item.Length);
			_ignoredItems = new List<Item>();
		}
		public override void Initialize()
		{
			ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
			ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInitialize);
			ServerApi.Hooks.NetGetData.Register(this, OnGetData);
			ServerApi.Hooks.NetSendData.Register(this, OnSendData);
		}
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
				ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInitialize);
				ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
				ServerApi.Hooks.NetSendData.Deregister(this, OnSendData);
			}
			base.Dispose(disposing);
		}
        private void OnInitialize(EventArgs args)
        {
            Commands.ChatCommands.Add(new Command("playeritemhistory.search", new CommandDelegate(PlayerItemHistoryReceive), new string[]
            {
                "pihr"
            }));
            Commands.ChatCommands.Add(new Command("playeritemhistory.search", new CommandDelegate(PlayerItemHistoryGive), new string[]
            {
                "pihg"
            }));
            Commands.ChatCommands.Add(new Command("playeritemhistory.reload", new CommandDelegate(PlayerItemHistoryReload), new string[]
            {
                "pihreload"
            }));
            Commands.ChatCommands.Add(new Command("playeritemhistory.flush", new CommandDelegate(PlayerItemHistoryFlush), new string[]
            {
                "pihflush"
            }));
        
            switch (TShock.Config.StorageType.ToLower())
            {
                case "mysql":
                    string[] host = TShock.Config.MySqlHost.Split(':');
                    DB = new MySqlConnection()
                    {
                        ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
                            host[0],
                            host.Length == 1 ? "3306" : host[1],
                            TShock.Config.MySqlDbName,
                            TShock.Config.MySqlUsername,
                            TShock.Config.MySqlPassword)
                    };
                    break;
                case "sqlite":
                    string sql = Path.Combine(TShock.SavePath, "ItemLog.sqlite");
                    DB = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
                    break;
            }
            SqlTableCreator sqlcreator = new SqlTableCreator(DB,
                DB.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());
            sqlcreator.EnsureTableStructure(new SqlTable("ItemLog",
                                    new SqlColumn("ID", MySqlDbType.Int32) { Unique = true, Primary = true },
                                    new SqlColumn("Timestamp", MySqlDbType.String, 19),
                                    new SqlColumn("ServerName", MySqlDbType.String, 64),
                                    new SqlColumn("SourcePlayerName", MySqlDbType.String, 30),
                                    new SqlColumn("SourceIP", MySqlDbType.String, 16),
                                    new SqlColumn("TargetPlayerName", MySqlDbType.String, 30),
                                    new SqlColumn("TargetIP", MySqlDbType.String, 16),
                                    new SqlColumn("Action", MySqlDbType.String, 16),
                                    new SqlColumn("DropX", MySqlDbType.Int32),
                                    new SqlColumn("DropY", MySqlDbType.Int32),
                                    new SqlColumn("ItemNetId", MySqlDbType.Int32),
                                    new SqlColumn("ItemName", MySqlDbType.String, 70),
                                    new SqlColumn("ItemStack", MySqlDbType.Int32),
                                    new SqlColumn("ItemPrefix", MySqlDbType.Int32),
                                    new SqlColumn("PlayerDrop", MySqlDbType.Int32),
                                    new SqlColumn("Pickup", MySqlDbType.Int32)
                                    ));
        }

        private void OnPostInitialize(EventArgs args)
		{
			SetupConfig();
		}
		private void OnGetData(GetDataEventArgs args)
		{
			if ((int)args.MsgID == 21)
			{
				TSPlayer tSPlayer = TShock.Players[args.Msg.whoAmI];
				using (MemoryStream memoryStream = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length))
				{
					using (BinaryReader binaryReader = new BinaryReader(memoryStream, Encoding.UTF8, true))
					{
						int num = (int)binaryReader.ReadInt16();
						float num2 = binaryReader.ReadSingle();
						float num3 = binaryReader.ReadSingle();
						binaryReader.ReadSingle();
						binaryReader.ReadSingle();
						int num4 = (int)binaryReader.ReadInt16();
						int num5 = (int)binaryReader.ReadByte();
						binaryReader.ReadBoolean();
						int num6 = (int)binaryReader.ReadInt16();
						if (num == 400)
						{
							Item itemById = TShock.Utils.GetItemById(num6);
							string name = tSPlayer.Name;
							string sourceIP = tSPlayer.IP.Split(new char[]
							{':'})[0];
							lock (_pendingLocker)
							{
								float dropX = num2 / 16f;
								float dropY = num3 / 16f;
								_playerDropsPending.Add(new ItemDrop(name, itemById.netID, num4, num5, dropX, dropY));
								if (CheckItem(itemById))
								{
									ItemDropLogger.CreateItemEntry(new ItemDropLogInfo("PlayerDrop", name, string.Empty, itemById.netID, num4, num5, dropX, dropY)
									{
										SourceIP = sourceIP
									});
								}
							}
						}
						if (num < 400 && num6 == 0)
						{
							Item item = Main.item[num];
							if (item.netID != 0)
							{
								string name2 = tSPlayer.Name;
								string targetIP = tSPlayer.IP.Split(new char[]
								{':'})[0];
								lock (_dropLocker)
								{
									ItemDrop itemDrop = _drops[num];
									if (_drops[num] != null && _drops[num].NetworkId != 0)
									{
										if (CheckItem(item))
										{
											ItemDropLogger.UpdateItemEntry(new ItemDropLogInfo("Pickup", itemDrop.SourceName, name2, (int)itemDrop.NetworkId, (int)itemDrop.Stack, (int)itemDrop.Prefix)
											{
												TargetIP = targetIP
											});
										}
										_drops[num] = null;
									}
								}
							}
						}
					}
				}
			}
		}
		private void OnSendData(SendDataEventArgs args)
		{
			if ((int)args.MsgId != 21)
			{
				return;
			}
			int number = args.number;
			if (_playerDropsPending.Count > 0 && number < 400)
			{
				Item item = Main.item[number];
				ItemDrop itemDrop = _playerDropsPending.FirstOrDefault((ItemDrop x) => x.NetworkId == item.netID && x.Stack == item.stack && x.Prefix == item.prefix);
				if (itemDrop != null)
				{
					lock (_dropLocker)
					{
						_drops[number] = itemDrop;
					}
					lock (_pendingLocker)
					{
						_playerDropsPending.Remove(itemDrop);
					}
				}
			}
		}
		private void PlayerItemHistoryReceive(CommandArgs args)
		{
			if (args.Parameters.Count == 0)
			{
				args.Player.SendErrorMessage("Invalid syntax! Proper syntax: /pihr <player> [page] [item id/name]");
				return;
			}
			string text = args.Parameters[0];
			List<TSPlayer> list = TShock.Utils.FindPlayer(text);
			string text2;
			if (list.Count == 0)
			{
				using (QueryResult queryResult = DbExt.QueryReader(DB, "SELECT COUNT(*) AS `Count` FROM `ItemLog` WHERE `TargetPlayerName`=@0", new object[]
				{
					text
				}))
				{
					if (!queryResult.Read() || queryResult.Get<int>("Count") <= 0)
					{
						args.Player.SendErrorMessage("Invalid player!");
						return;
					}
				}
				text2 = text;
			}
			if (list.Count <= 1)
			{
				text2 = list[0].Name;
			}
			TShock.Utils.SendMultipleMatchError(args.Player, 
				from p in list
				select p.Name);
			return;
			IL_E3:
			int num;
			if (args.Parameters.Count < 2 || !int.TryParse(args.Parameters[1], out num) || num < 0)
			{
				num = 1;
			}
			Item item = null;
			if (args.Parameters.Count >= 3)
			{
				List<Item> itemByIdOrName = TShock.Utils.GetItemByIdOrName(args.Parameters[2]);
				if (itemByIdOrName.Count == 0)
				{
					args.Player.SendErrorMessage("Invalid item!");
					return;
				}
				if (itemByIdOrName.Count > 1)
				{
					TShock.Utils.SendMultipleMatchError(args.Player, 
						from x in itemByIdOrName
						select x.name);
					return;
				}
				item = itemByIdOrName[0];
			}
			QueryResult queryResult2;
			if (item != null)
			{
				queryResult2 = DbExt.QueryReader(DB, "SELECT * FROM `ItemLog` WHERE `TargetPlayerName`=@0 AND `ItemNetId`=@1 ORDER BY `Timestamp` DESC LIMIT @2,@3", new object[]
				{
					text2,
					item.netID,
					(num - 1) * 5,
					5
				});
			}
			else
			{
				queryResult2 = DbExt.QueryReader(DB, "SELECT * FROM `ItemLog` WHERE `TargetPlayerName`=@0 ORDER BY `Timestamp` DESC LIMIT @1,@2", new object[]
				{
					text2,
					(num - 1) * 5,
					5
				});
			}
			using (queryResult2)
			{
				args.Player.SendInfoMessage("Item Drop Log - v{0} - by IcyTerraria", new object[]
				{
					Version
				});
				args.Player.SendInfoMessage("Results for {0}:", new object[]
				{
					text2
				});
				int num2 = (num - 1) * 5;
				DateTime now = DateTime.Now;
				while (queryResult2.Read())
				{
					Item itemById = TShock.Utils.GetItemById(queryResult2.Get<int>("ItemNetId"));
                    string s = queryResult2.Get<string>("Timestamp");
                    string text3 = queryResult2.Get<string>("ServerName");
                    string text4 = queryResult2.Get<string>("SourcePlayerName");
                    string text5 = queryResult2.Get<string>("TargetPlayerName");
                    string value = queryResult2.Get<string>("ItemName");
                    int num3 = queryResult2.Get<int>("ItemStack");
                    string text6 = queryResult2.Get<string>("ItemPrefix");
                    StringBuilder stringBuilder = new StringBuilder();
                    if (text6 != "None")
                    {
						stringBuilder.Append(text6).Append(' ');
					}
					stringBuilder.Append(value);
					if (itemById.maxStack > 1)
					{
						stringBuilder.Append(' ').AppendFormat("({0}/{1})", num3, itemById.maxStack);
					}
					string text7 = string.Empty;
					if (!string.IsNullOrEmpty(text3))
					{
						text7 = " on " + text3;
					}
					DateTime d = DateTime.Parse(s);
					TimeSpan span = now - d;
					args.Player.SendInfoMessage("{0}. {1} received {2} from {3}{4} ({5} ago)", new object[]
					{
						++num2,
                        text5,
						stringBuilder.ToString(),
                        text4,
						text7,
						TimeSpanToDurationString(span)
					});
				}
			}
		}
		private void PlayerItemHistoryGive(CommandArgs args)
		{
			if (args.Parameters.Count == 0)
			{
				args.Player.SendErrorMessage("Invalid syntax! Proper syntax: /pihg <player> [page] [item id/name]");
				return;
			}
			string text = args.Parameters[0];
			List<TSPlayer> list = TShock.Utils.FindPlayer(text);
			string text2;
			if (list.Count == 0)
			{
				using (QueryResult queryResult = DbExt.QueryReader(DB, "SELECT COUNT(*) AS `Count` FROM `ItemLog` WHERE `SourcePlayerName`=@0", new object[]
				{
					text
				}))
				{
					if (!queryResult.Read() || queryResult.Get<int>("Count") <= 0)
					{
						args.Player.SendErrorMessage("Invalid player!");
						return;
					}
				}
				text2 = text;
			}
			if (list.Count <= 1)
			{
				text2 = list[0].Name;
			}
			TShock.Utils.SendMultipleMatchError(args.Player, 
				from p in list
				select p.Name);
			return;
			int num;
			if (args.Parameters.Count < 2 || !int.TryParse(args.Parameters[1], out num) || num < 0)
			{
				num = 1;
			}
			Item item = null;
			if (args.Parameters.Count >= 3)
			{
				List<Item> itemByIdOrName = TShock.Utils.GetItemByIdOrName(args.Parameters[2]);
				if (itemByIdOrName.Count == 0)
				{
					args.Player.SendErrorMessage("Invalid item!");
					return;
				}
				if (itemByIdOrName.Count > 1)
				{
					TShock.Utils.SendMultipleMatchError(args.Player, 
						from x in itemByIdOrName
						select x.name);
					return;
				}
				item = itemByIdOrName[0];
			}
			QueryResult queryResult2;
			if (item != null)
			{
				queryResult2 = DbExt.QueryReader(DB, "SELECT * FROM `ItemLog` WHERE `SourcePlayerName`=@0 AND `ItemNetId`=@1 ORDER BY `Timestamp` DESC LIMIT @2,@3", new object[]
				{
					text2,
					item.netID,
					(num - 1) * 5,
					5
				});
			}
			else
			{
				queryResult2 = DbExt.QueryReader(DB, "SELECT * FROM `ItemLog` WHERE `SourcePlayerName`=@0 ORDER BY `Timestamp` DESC LIMIT @1,@2", new object[]
				{
					text2,
					(num - 1) * 5,
					5
				});
			}
			using (queryResult2)
			{
				args.Player.SendInfoMessage("Item Drop Log - v{0} - by IcyTerraria", new object[]
				{
					Version
				});
				args.Player.SendInfoMessage("Results for {0}:", new object[]
				{
					text2
				});
				int num2 = (num - 1) * 5;
				DateTime now = DateTime.Now;
				while (queryResult2.Read())
				{
					Item itemById = TShock.Utils.GetItemById(queryResult2.Get<int>("ItemNetId"));
                    string s = queryResult2.Get<string>("Timestamp");
                    string text3 = queryResult2.Get<string>("ServerName");
                    string text4 = queryResult2.Get<string>("SourcePlayerName");
                    string text5 = queryResult2.Get<string>("TargetPlayerName");
                    string value = queryResult2.Get<string>("ItemName");
                    int num3 = queryResult2.Get<int>("ItemStack");
                    string text6 = queryResult2.Get<string>("ItemPrefix");
                    StringBuilder stringBuilder = new StringBuilder();
                    if (text6 != "None")
                    {
						stringBuilder.Append(text6).Append(' ');
					}
					stringBuilder.Append(value);
					if (itemById.maxStack > 1)
					{
						stringBuilder.Append(' ').AppendFormat("({0}/{1})", num3, itemById.maxStack);
					}
					string text7 = string.Empty;
					if (!string.IsNullOrEmpty(text3))
					{
						text7 = " on " + text3;
					}
					DateTime d = DateTime.Parse(s);
					TimeSpan span = now - d;
					args.Player.SendInfoMessage("{0}. {1} gave {2} to {3}{4} ({5} ago)", new object[]
					{
						++num2,
                        text4,
						stringBuilder.ToString(),
                        text5,
						text7,
						TimeSpanToDurationString(span)
					});
				}
			}
		}
		private void PlayerItemHistoryReload(CommandArgs args)
		{
			LoadConfig(ItemDropLogPlugin.ConfigPath);
			args.Player.SendInfoMessage("PlayerItemHistory config reloaded.");
		}
		private void PlayerItemHistoryFlush(CommandArgs args)
		{
			if (args.Parameters.Count == 0)
			{
				args.Player.SendErrorMessage("Invalid syntax! Proper syntax: /pihflush <days>");
				return;
			}
			int num;
			if (!int.TryParse(args.Parameters[0], out num) || num < 1)
			{
				args.Player.SendErrorMessage("Invalid days");
				return;
			}
			DateTime dateTime = DateTime.Now.AddDays((double)(-(double)num));
			int num2 = DbExt.Query(DB, "DELETE FROM `ItemLog` WHERE `Timestamp`<@0 AND `ServerName`=@1", new object[]
			{
				dateTime.ToString("s"),
				TShock.Config.ServerName
			});
			args.Player.SendInfoMessage("Successfully flushed {0:n0} rows from the database.", new object[]
			{
				num2
			});
		}
		private string TimeSpanToDurationString(TimeSpan span)
		{
			int days = span.Days;
			int hours = span.Hours;
			int minutes = span.Minutes;
			int seconds = span.Seconds;
			List<string> list = new List<string>(4);
			if (days > 0)
			{
				list.Add(days + "d");
			}
			if (hours > 0)
			{
				list.Add(hours + "h");
			}
			if (minutes > 0)
			{
				list.Add(minutes + "m");
			}
			if (seconds > 0)
			{
				list.Add(seconds + "s");
			}
			return string.Join(" ", list);
		}
		private string GetPrefixName(int pre)
		{
			string result = "None";
			if (pre > 0)
			{
				result = Lang.prefix[pre];
			}
			return result;
		}
		private void SetupConfig()
		{
			try
			{
				if (File.Exists(ItemDropLogPlugin.ConfigPath))
				{
					LoadConfig(ItemDropLogPlugin.ConfigPath);
				}
				else
				{
					TShock.Log.ConsoleError("ItemDropLog configuration not found. Using default configuration.");
					LoadConfig(null);
					Config.SaveInstance(ItemDropLogPlugin.ConfigPath);
				}
			}
			catch (Exception ex)
			{
                TShock.Log.ConsoleError(ex.ToString());
			}
		}
		private void LoadConfig(string path)
		{
			Config.CreateInstance(path);
			_ignoredItems.Clear();
			foreach (string current in Config.Instance.IgnoredItems)
			{
				List<Item> itemByIdOrName = TShock.Utils.GetItemByIdOrName(current);
				if (itemByIdOrName.Count > 0)
				{
					_ignoredItems.Add(itemByIdOrName[0]);
				}
			}
		}
		private bool CheckItem(Item item)
		{
			return _ignoredItems.All((Item x) => x.netID != item.netID);
		}
	}
}
