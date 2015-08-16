using System;
using Terraria;
using TShockAPI;
namespace ItemDropLog
{
	public struct ItemDropLogInfo
	{
		public DateTime Timestamp;
		public string ServerName;
		public string SourcePlayerName;
		public string SourceIP;
		public string TargetPlayerName;
		public string TargetIP;
		public string Action;
		public int ItemNetId;
		public string ItemName;
		public int ItemStack;
		public string ItemPrefix;
		public float DropX;
		public float DropY;
		public bool IsValid
		{
			get
			{
				return !string.IsNullOrEmpty(ServerName) && !string.IsNullOrEmpty(Action) && !string.IsNullOrEmpty(SourcePlayerName) && !string.IsNullOrEmpty(TargetPlayerName) && ItemNetId != 0;
			}
		}
		public ItemDropLogInfo(string action, string sourcePlayerName, string targetPlayerName, int itemNetId, int itemStack, int itemPrefix)
		{
			this = new ItemDropLogInfo(action, sourcePlayerName, targetPlayerName, itemNetId, itemStack, itemPrefix, 0f, 0f);
		}
		public ItemDropLogInfo(string action, string sourcePlayerName, string targetPlayerName, int itemNetId, int itemStack, int itemPrefix, float dropX, float dropY)
		{
			Timestamp = DateTime.Now;
			ServerName = TShock.Config.ServerName;
			SourcePlayerName = sourcePlayerName;
			SourceIP = string.Empty;
			TargetPlayerName = targetPlayerName;
			TargetIP = string.Empty;
			Action = action;
			ItemNetId = itemNetId;
			ItemName = string.Empty;
			ItemStack = itemStack;
			ItemPrefix = "None";
			DropX = dropX;
			DropY = dropY;
			if (itemNetId != 0)
			{
				ItemName = GetItemName(itemNetId);
				if (itemPrefix != 0)
				{
					ItemPrefix = GetPrefixName(itemPrefix);
				}
			}
		}
		private string GetItemName(int netId)
		{
			Item itemById = TShock.Utils.GetItemById(netId);
			if (itemById != null && itemById.netID == netId)
			{
				return itemById.name;
			}
			return string.Empty;
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
	}
}
