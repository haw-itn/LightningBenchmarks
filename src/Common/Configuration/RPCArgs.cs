﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Configuration
{
	public class RPCArgs
	{
		public Uri Url
		{
			get; set;
		}
		public string User
		{
			get; set;
		}
		public string Password
		{
			get; set;
		}
		public string CookieFile
		{
			get; set;
		}
		public bool NoTest
		{
			get;
			set;
		}
		public string AuthenticationString
		{
			get;
			set;
		}

		public RPCClient ConfigureRPCClient(Network network)
		{
			RPCClient rpcClient = null;
			var url = Url;
			var usr = User;
			var pass = Password;
			if(url != null && usr != null && pass != null)
				rpcClient = new RPCClient(new System.Net.NetworkCredential(usr, pass), url, network);
			if(rpcClient == null)
			{
				if(CookieFile != null)
				{
					try
					{
						rpcClient = new RPCClient(new RPCCredentialString() { CookieFile = CookieFile }, url, network);
					}
					catch(IOException)
					{
						Common.Logging.CommonLogs.Configuration.LogWarning($"RPC Cookie file not found at " + (CookieFile ?? RPCClient.GetDefaultCookieFilePath(network)));
					}
				}

				if(AuthenticationString != null)
				{
					rpcClient = new RPCClient(RPCCredentialString.Parse(AuthenticationString), url, network);
				}

				if(rpcClient == null)
				{
					try
					{
						rpcClient = new RPCClient(null as NetworkCredential, url, network);
					}
					catch { }
					if(rpcClient == null)
					{
						Common.Logging.CommonLogs.Configuration.LogError($"RPC connection settings not configured");
						throw new ConfigException();
					}
				}
			}
			return rpcClient;
		}

		public static async Task TestRPCAsync(Network network, RPCClient rpcClient, CancellationToken cancellation)
		{
			Common.Logging.CommonLogs.Configuration.LogInformation("Testing RPC connection to " + rpcClient.Address.AbsoluteUri);
			try
			{
				var address = new Key().PubKey.GetAddress(network);
				int time = 0;
				while(true)
				{
					time++;
					try
					{

						var isValid = ((JObject)(await rpcClient.SendCommandAsync("validateaddress", address.ToString())).Result)["isvalid"].Value<bool>();
						if(!isValid)
						{
							Common.Logging.CommonLogs.Configuration.LogError("The RPC Server is on a different blockchain than the one configured for tumbling");
							throw new ConfigException();
						}
						break;
					}
					catch(RPCException ex) when(IsTransient(ex))
					{
						Common.Logging.CommonLogs.Configuration.LogInformation($"Transient error '{ex.Message}', retrying soon...");
						await Task.Delay(Math.Min(1000 * time, 10000), cancellation);
					}
				}
			}
			catch(ConfigException)
			{
				throw;
			}
			catch(RPCException ex)
			{
				Common.Logging.CommonLogs.Configuration.LogError("Invalid response from RPC server " + ex.Message);
				throw new ConfigException();
			}
			catch(Exception ex)
			{
				Common.Logging.CommonLogs.Configuration.LogError("Error connecting to RPC server " + ex.Message);
				throw new ConfigException();
			}
			Common.Logging.CommonLogs.Configuration.LogInformation("RPC connection successfull");
			int version = await GetVersion(rpcClient);
			Common.Logging.CommonLogs.Configuration.LogInformation($"Bitcoin version detected: {version}");
		}

		private static async Task<int> GetVersion(RPCClient rpcClient)
		{
			try
			{
				var getInfo = await rpcClient.SendCommandAsync(RPCOperations.getnetworkinfo);
				return ((JObject)getInfo.Result)["version"].Value<int>();
			}
			catch(RPCException ex) when(ex.RPCCode == RPCErrorCode.RPC_METHOD_NOT_FOUND)
			{
#pragma warning disable CS0618 // Type or member is obsolete
				var getInfo = await rpcClient.SendCommandAsync(RPCOperations.getinfo);
#pragma warning restore CS0618 // Type or member is obsolete
				return ((JObject)getInfo.Result)["version"].Value<int>();
			}
		}

		private static bool IsTransient(RPCException ex)
		{
			return
				   ex.RPCCode == RPCErrorCode.RPC_IN_WARMUP ||
				   ex.Message.Contains("Loading wallet...") ||
				   ex.Message.Contains("Loading block index...") ||
				   ex.Message.Contains("Loading P2P addresses...") ||
				   ex.Message.Contains("Rewinding blocks...") ||
				   ex.Message.Contains("Verifying blocks...") ||
				   ex.Message.Contains("Loading addresses...");
		}

		public static void CheckNetwork(Network network, RPCClient rpcClient)
		{
			if(network.GenesisHash != null && rpcClient.GetBlockHash(0) != network.GenesisHash)
			{
				Common.Logging.CommonLogs.Configuration.LogError("The RPC server is not using the chain " + network.Name);
				throw new ConfigException();
			}
		}

		public static RPCArgs Parse(IConfiguration confArgs, Network network, string prefix = null)
		{
			prefix = prefix ?? "";
			if(prefix != "")
			{
				if(!prefix.EndsWith("."))
					prefix += ".";
			}
			try
			{
				var url = confArgs.GetOrDefault<string>(prefix + "rpc.url", network == null ? null : "http://localhost:" + network.RPCPort + "/");
				return new RPCArgs()
				{
					User = confArgs.GetOrDefault<string>(prefix + "rpc.user", null),
					Password = confArgs.GetOrDefault<string>(prefix + "rpc.password", null),
					CookieFile = confArgs.GetOrDefault<string>(prefix + "rpc.cookiefile", null),
					AuthenticationString = confArgs.GetOrDefault<string>(prefix + "rpc.auth", null),
					Url = url == null ? null : new Uri(url)
				};
			}
			catch(FormatException)
			{
				throw new ConfigException("rpc.url is not an url");
			}
		}
	}
}
