﻿/*
  KeePass Password Safe - The Open-Source Password Manager
  Copyright (C) 2003-2007 Dominik Reichl <dominik.reichl@t-online.de>

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

using KeePass.App;
using KeePass.Forms;
using KeePass.Resources;
using KeePass.Util;

using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

namespace KeePass
{
	public static class Program
	{
		private static CommandLineArgs m_cmdLineArgs = new CommandLineArgs();
		private static Random m_rndGlobal = null;
		private static uint m_uAppMessage = 0;
		private static MainForm m_formMain = null;

		public enum AppMessage
		{
			RestoreWindow = 0
		}

		public static CommandLineArgs CommandLineArgs
		{
			get { return m_cmdLineArgs; }
		}

		public static Random GlobalRandom
		{
			get { return m_rndGlobal; }
		}

		public static uint ApplicationMessage
		{
			get { return m_uAppMessage; }
		}

		public static MainForm MainForm
		{
			get { return m_formMain; }
		}

		/// <summary>
		/// Main entry point for the application.
		/// </summary>
		[STAThread]
		public static void Main(string[] args)
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			m_rndGlobal = new Random((int)DateTime.Now.Ticks);

			string strHelpFile = UrlUtil.StripExtension(WinUtil.GetExecutable()) +
				".chm";
			AppHelp.LocalHelpFile = strHelpFile;

			// Set global localized strings
			PwDatabase.LocalizedAppName = PwDefs.ShortProductName;
			Kdb4File.DetermineLanguageID();
			StrUtil.SetLocalizedString(StrUtil.LocalizedStringID.ExceptionOccured,
				KPRes.ExceptionOccured);

			m_cmdLineArgs.Parse(args);
			m_cmdLineArgs.Lock();

			if(m_cmdLineArgs[AppDefs.CommandLineOptions.FileExtRegister] != null)
			{
				ShellUtil.RegisterExtension(AppDefs.FileExtension.FileExt, AppDefs.FileExtension.ExtID,
					KPRes.FileExtName, WinUtil.GetExecutable(), PwDefs.ShortProductName, false);
				return;
			}
			else if(m_cmdLineArgs[AppDefs.CommandLineOptions.FileExtUnregister] != null)
			{
				ShellUtil.UnregisterExtension(AppDefs.FileExtension.FileExt, AppDefs.FileExtension.ExtID);
				return;
			}

			AppConfigEx.Load();
			if(AppConfigEx.GetBool(AppDefs.ConfigKeys.EnableLogging))
				AppLogEx.Open(PwDefs.ShortProductName);

			m_uAppMessage = WinUtil.RegisterMessage("EB2FE38E1A6A4A138CF561442F1CF25A");

			Mutex mSingleLock = TrySingleInstanceLock();
			if((mSingleLock == null) && AppConfigEx.GetBool(AppDefs.ConfigKeys.LimitSingleInstance))
			{
				ActivatePreviousInstance();
				return;
			}

#if DEBUG
			m_formMain = new MainForm();
			Application.Run(m_formMain);
#else
			try
			{
				m_formMain = new MainForm();
				Application.Run(m_formMain);
			}
			catch(Exception ex)
			{
				string str = KPRes.ExceptionOccured + " ";
				str += KPRes.ProgramTerminates + "\r\n\r\n";
				str += StrUtil.FormatException(ex, false);

				MessageBox.Show(str, PwDefs.ShortProductName, MessageBoxButtons.OK,
					MessageBoxIcon.Stop);
			}
#endif

			AppLogEx.Close();
			if(mSingleLock != null) { GC.KeepAlive(mSingleLock); }
		}

		private static Mutex TrySingleInstanceLock()
		{
			bool bCreatedNew;

			try
			{
				Mutex mSingleLock = new Mutex(true, AppDefs.MutexName, out bCreatedNew);

				if(!bCreatedNew) return null;

				return mSingleLock;
			}
			catch(Exception) { }

			return null;
		}

		private static void ActivatePreviousInstance()
		{
			WinUtil.SendMessage((IntPtr)WinUtil.HWND_BROADCAST, m_uAppMessage,
				(IntPtr)AppMessage.RestoreWindow, IntPtr.Zero);
		}
	}
}
