﻿/*
  KeePass Password Safe - The Open-Source Password Manager
  Copyright (C) 2003-2009 Dominik Reichl <dominik.reichl@t-online.de>

  This program is free software; you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation; either version 2 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with this program; if not, write to the Free Software
  Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
*/

using System;
using System.Collections;
using System.Windows.Forms;
using System.Globalization;
using System.Threading;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.IO;

using KeePass.App;
using KeePass.App.Configuration;
using KeePass.DataExchange;
using KeePass.Forms;
using KeePass.Native;
using KeePass.Resources;
using KeePass.UI;
using KeePass.Util;
using KeePass.Ecas;
using KeePass.Plugins;

using KeePassLib;
using KeePassLib.Cryptography;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.PasswordGenerator;
using KeePassLib.Keys;
using KeePassLib.Resources;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Translation;
using KeePassLib.Utility;

namespace KeePass
{
	public static class Program
	{
		private const string m_strWndMsgID = "EB2FE38E1A6A4A138CF561442F1CF25A";

		private static CommandLineArgs m_cmdLineArgs = null;
		private static Random m_rndGlobal = null;
		private static int m_nAppMessage = 0;
		private static MainForm m_formMain = null;
		private static AppConfigEx m_appConfig = null;
		private static KeyProviderPool m_keyProviderPool = null;
		private static KeyValidatorPool m_keyValidatorPool = null;
		private static FileFormatPool m_fmtPool = null;
		private static KPTranslation m_kpTranslation = new KPTranslation();
		private static TempFilesPool m_tempFilesPool = null;
		private static EcasPool m_ecasPool = null;
		private static EcasTriggerSystem m_ecasTriggers = null;
		private static CustomPwGeneratorPool m_pwGenPool = null;

		public enum AppMessage
		{
			Null = 0,
			RestoreWindow = 1,
			Exit = 2,
			IpcByFile = 3,
			AutoType = 4
		}

		public static CommandLineArgs CommandLineArgs
		{
			get
			{
				if(m_cmdLineArgs == null) m_cmdLineArgs = new CommandLineArgs(null);
				return m_cmdLineArgs;
			}
		}

		public static Random GlobalRandom
		{
			get { return m_rndGlobal; }
		}

		public static int ApplicationMessage
		{
			get { return m_nAppMessage; }
		}

		public static MainForm MainForm
		{
			get { return m_formMain; }
		}

		public static AppConfigEx Config
		{
			get
			{
				if(m_appConfig == null) m_appConfig = new AppConfigEx();
				return m_appConfig;
			}
		}

		public static KeyProviderPool KeyProviderPool
		{
			get
			{
				if(m_keyProviderPool == null) m_keyProviderPool = new KeyProviderPool();
				return m_keyProviderPool;
			}
		}

		public static KeyValidatorPool KeyValidatorPool
		{
			get
			{
				if(m_keyValidatorPool == null) m_keyValidatorPool = new KeyValidatorPool();
				return m_keyValidatorPool;
			}
		}

		public static FileFormatPool FileFormatPool
		{
			get
			{
				if(m_fmtPool == null) m_fmtPool = new FileFormatPool();
				return m_fmtPool;
			}
		}

		public static KPTranslation Translation
		{
			get { return m_kpTranslation; }
		}

		public static TempFilesPool TempFilesPool
		{
			get
			{
				if(m_tempFilesPool == null) m_tempFilesPool = new TempFilesPool();
				return m_tempFilesPool;
			}
		}

		public static EcasPool EcasPool // Construct on first access
		{
			get
			{
				if(m_ecasPool == null) m_ecasPool = new EcasPool(true);
				return m_ecasPool;
			}
		}

		public static EcasTriggerSystem TriggerSystem
		{
			get
			{
				if(m_ecasTriggers == null) m_ecasTriggers = new EcasTriggerSystem();
				return m_ecasTriggers;
			}
		}

		public static CustomPwGeneratorPool PwGeneratorPool
		{
			get
			{
				if(m_pwGenPool == null) m_pwGenPool = new CustomPwGeneratorPool();
				return m_pwGenPool;
			}
		}

		/// <summary>
		/// Main entry point for the application.
		/// </summary>
		[STAThread]
		public static void Main(string[] args)
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.DoEvents(); // Required

			int nRandomSeed = (int)DateTime.Now.Ticks;
			// Prevent overflow (see Random class constructor)
			if(nRandomSeed == int.MinValue) nRandomSeed = 17;
			m_rndGlobal = new Random(nRandomSeed);

			// Set global localized strings
			PwDatabase.LocalizedAppName = PwDefs.ShortProductName;
			Kdb4File.DetermineLanguageId();

			m_appConfig = AppConfigSerializer.Load();
			if(m_appConfig.Logging.Enabled)
				AppLogEx.Open(PwDefs.ShortProductName);

			AppPolicy.Current = m_appConfig.Security.Policy.CloneDeep();

			m_ecasTriggers = m_appConfig.Application.TriggerSystem;
			m_ecasTriggers.SetToInitialState();

			string strHelpFile = UrlUtil.StripExtension(WinUtil.GetExecutable()) + ".chm";
			AppHelp.LocalHelpFile = strHelpFile;

			string strLangFile = m_appConfig.Application.LanguageFile;
			if((strLangFile != null) && (strLangFile.Length > 0))
			{
				strLangFile = UrlUtil.GetFileDirectory(WinUtil.GetExecutable(), true,
					false) + strLangFile;

				try
				{
					m_kpTranslation = KPTranslation.LoadFromFile(strLangFile);

					KPRes.SetTranslatedStrings(
						m_kpTranslation.SafeGetStringTableDictionary(
						"KeePass.Resources.KPRes"));
					KLRes.SetTranslatedStrings(
						m_kpTranslation.SafeGetStringTableDictionary(
						"KeePassLib.Resources.KLRes"));
				}
				catch(FileNotFoundException) { } // Ignore
				catch(Exception) { Debug.Assert(false); }
			}

			if(m_appConfig.Application.Start.PluginCacheClearOnce)
			{
				PlgxCache.Clear();
				m_appConfig.Application.Start.PluginCacheClearOnce = false;
				AppConfigSerializer.Save(Program.Config);
			}

			m_cmdLineArgs = new CommandLineArgs(args);

			if(m_cmdLineArgs[AppDefs.CommandLineOptions.FileExtRegister] != null)
			{
				ShellUtil.RegisterExtension(AppDefs.FileExtension.FileExt, AppDefs.FileExtension.ExtId,
					KPRes.FileExtName, WinUtil.GetExecutable(), PwDefs.ShortProductName, false);
				MainCleanUp();
				return;
			}
			else if(m_cmdLineArgs[AppDefs.CommandLineOptions.FileExtUnregister] != null)
			{
				ShellUtil.UnregisterExtension(AppDefs.FileExtension.FileExt, AppDefs.FileExtension.ExtId);
				MainCleanUp();
				return;
			}
			else if((m_cmdLineArgs[AppDefs.CommandLineOptions.Help] != null) ||
				(m_cmdLineArgs[AppDefs.CommandLineOptions.HelpLong] != null))
			{
				AppHelp.ShowHelp(AppDefs.HelpTopics.CommandLine, null);
				MainCleanUp();
				return;
			}
			else if(m_cmdLineArgs[AppDefs.CommandLineOptions.ConfigSetUrlOverride] != null)
			{
				Program.Config.Integration.UrlOverride = m_cmdLineArgs[
					AppDefs.CommandLineOptions.ConfigSetUrlOverride];
				AppConfigSerializer.Save(Program.Config);
				MainCleanUp();
				return;
			}
			else if(m_cmdLineArgs[AppDefs.CommandLineOptions.ConfigClearUrlOverride] != null)
			{
				Program.Config.Integration.UrlOverride = string.Empty;
				AppConfigSerializer.Save(Program.Config);
				MainCleanUp();
				return;
			}
			else if(m_cmdLineArgs[AppDefs.CommandLineOptions.ConfigGetUrlOverride] != null)
			{
				try
				{
					string strFileOut = UrlUtil.EnsureTerminatingSeparator(
						Path.GetTempPath(), false) + "KeePass_UrlOverride.tmp";
					string strContent = ("[KeePass]\r\nKeeURLOverride=" +
						Program.Config.Integration.UrlOverride + "\r\n");
					File.WriteAllText(strFileOut, strContent);
				}
				catch(Exception) { Debug.Assert(false); }
				MainCleanUp();
				return;
			}
			else if(m_cmdLineArgs[AppDefs.CommandLineOptions.ConfigSetLanguageFile] != null)
			{
				Program.Config.Application.LanguageFile = m_cmdLineArgs[
					AppDefs.CommandLineOptions.ConfigSetLanguageFile];
				AppConfigSerializer.Save(Program.Config);
				MainCleanUp();
				return;
			}
			else if(m_cmdLineArgs[AppDefs.CommandLineOptions.PlgxCreate] != null)
			{
				PlgxPlugin.CreateFromCommandLine();
				MainCleanUp();
				return;
			}
			else if(m_cmdLineArgs[AppDefs.CommandLineOptions.PlgxCreateInfo] != null)
			{
				PlgxPlugin.CreateInfoFile(m_cmdLineArgs.FileName);
				MainCleanUp();
				return;
			}

			try { m_nAppMessage = NativeMethods.RegisterWindowMessage(m_strWndMsgID); }
			catch(Exception) { Debug.Assert(false); }

			if(m_cmdLineArgs[AppDefs.CommandLineOptions.ExitAll] != null)
			{
				BroadcastAppMessageAndCleanUp(AppMessage.Exit);
				return;
			}
			else if(m_cmdLineArgs[AppDefs.CommandLineOptions.AutoType] != null)
			{
				BroadcastAppMessageAndCleanUp(AppMessage.AutoType);
				return;
			}
			else if(m_cmdLineArgs[AppDefs.CommandLineOptions.OpenEntryUrl] != null)
			{
				string strEntryUuid = m_cmdLineArgs[AppDefs.CommandLineOptions.Uuid];
				if(!string.IsNullOrEmpty(strEntryUuid))
				{
					IpcParamEx ipUrl = new IpcParamEx(IpcUtilEx.CmdOpenEntryUrl,
						strEntryUuid, null, null, null, null);
					IpcUtilEx.SendGlobalMessage(ipUrl);
				}

				MainCleanUp();
				return;
			}

			Mutex mSingleLock = TrySingleInstanceLock(AppDefs.MutexName, true);
			if((mSingleLock == null) && m_appConfig.Integration.LimitToSingleInstance)
			{
				ActivatePreviousInstance(args);
				MainCleanUp();
				return;
			}

			Mutex mGlobalNotify = TryGlobalInstanceNotify(AppDefs.MutexNameGlobal);

			bool bRunMainWindow = true;
			try { SelfTest.Perform(); }
			catch(Exception exSelfTest)
			{
				MessageService.ShowWarning(KPRes.SelfTestFailed, exSelfTest);
				bRunMainWindow = false;
			}

			UserActivityNotifyFilter nfActivity = new UserActivityNotifyFilter();
			Application.AddMessageFilter(nfActivity);

			if(bRunMainWindow)
			{
#if DEBUG
				m_formMain = new MainForm();
				Application.Run(m_formMain);
#else
				try
				{
					m_formMain = new MainForm();
					Application.Run(m_formMain);
				}
				catch(Exception exPrg) { MessageService.ShowFatal(exPrg); }
#endif
			}

			Application.RemoveMessageFilter(nfActivity);

			Debug.Assert(GlobalWindowManager.WindowCount == 0);
			Debug.Assert(MessageService.CurrentMessageCount == 0);

			MainCleanUp();

			if(mGlobalNotify != null) { GC.KeepAlive(mGlobalNotify); }
			if(mSingleLock != null) { GC.KeepAlive(mSingleLock); }
		}

		private static void MainCleanUp()
		{
			if(m_tempFilesPool != null) m_tempFilesPool.Clear();

			EntryMenu.Destroy();

			AppLogEx.Close();
		}

		internal static Mutex TrySingleInstanceLock(string strName, bool bInitiallyOwned)
		{
			if(strName == null) throw new ArgumentNullException("strName");

			try
			{
				bool bCreatedNew;
				Mutex mSingleLock = new Mutex(bInitiallyOwned, strName, out bCreatedNew);

				if(!bCreatedNew) return null;

				return mSingleLock;
			}
			catch(Exception) { }

			return null;
		}

		internal static Mutex TryGlobalInstanceNotify(string strBaseName)
		{
			if(strBaseName == null) throw new ArgumentNullException("strBaseName");

			try
			{
				string strName = "Global\\" + strBaseName;
				string strIdentity = Environment.UserDomainName + "\\" +
					Environment.UserName;
				MutexSecurity ms = new MutexSecurity();

				MutexAccessRule mar = new MutexAccessRule(strIdentity,
					MutexRights.FullControl, AccessControlType.Allow);
				ms.AddAccessRule(mar);

				SecurityIdentifier sid = new SecurityIdentifier(
					WellKnownSidType.WorldSid, null);
				mar = new MutexAccessRule(sid, MutexRights.ReadPermissions |
					MutexRights.Synchronize, AccessControlType.Allow);
				ms.AddAccessRule(mar);

				bool bCreatedNew;
				return new Mutex(false, strName, out bCreatedNew, ms);
			}
			catch(Exception) { } // Windows 9x and Mono 2.0+ (AddAccessRule) throw

			return null;
		}

		internal static void DestroyMutex(Mutex m, bool bReleaseFirst)
		{
			if(m == null) return;

			if(bReleaseFirst)
			{
				try { m.ReleaseMutex(); }
				catch(Exception) { Debug.Assert(false); }
			}

			try { m.Close(); }
			catch(Exception) { Debug.Assert(false); }
		}

		private static void ActivatePreviousInstance(string[] args)
		{
			if(m_nAppMessage == 0) { Debug.Assert(false); return; }

			try
			{
				if(string.IsNullOrEmpty(m_cmdLineArgs.FileName))
					NativeMethods.PostMessage((IntPtr)NativeMethods.HWND_BROADCAST,
						m_nAppMessage, (IntPtr)AppMessage.RestoreWindow, IntPtr.Zero);
				else
				{
					IpcParamEx ipcMsg = new IpcParamEx(IpcUtilEx.CmdOpenDatabase,
						CommandLineArgs.SafeSerialize(args), null, null, null, null);

					IpcUtilEx.SendGlobalMessage(ipcMsg);
				}
			}
			catch(Exception) { Debug.Assert(false); }
		}

		// For plugins
		public static void NotifyUserActivity()
		{
			if(Program.m_formMain != null) Program.m_formMain.NotifyUserActivity();
		}

		public static IntPtr GetSafeMainWindowHandle()
		{
			if(m_formMain == null) return IntPtr.Zero;

			try { return m_formMain.Handle; }
			catch(Exception) { Debug.Assert(false); }

			return IntPtr.Zero;
		}

		private static void BroadcastAppMessageAndCleanUp(AppMessage msg)
		{
			try
			{
				NativeMethods.PostMessage((IntPtr)NativeMethods.HWND_BROADCAST,
					m_nAppMessage, (IntPtr)msg, IntPtr.Zero);
			}
			catch(Exception) { Debug.Assert(false); }

			MainCleanUp();
		}
	}
}
