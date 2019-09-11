/*
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
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

using Microsoft.Win32;

using KeePass.Util.Spr;

using KeePassLib.Security;
using KeePassLib.Utility;

namespace KeePass.Util
{
	public static class AppLocator
	{
		private static string m_strIE = null;
		private static string m_strFirefox = null;
		private static string m_strOpera = null;
		private static string m_strChrome = null;

		public static string InternetExplorerPath
		{
			get
			{
				if(m_strIE != null) return m_strIE;
				else
				{
					try { m_strIE = FindInternetExplorer(); }
					catch(Exception) { m_strIE = null; }

					return m_strIE;
				}
			}
		}

		public static string FirefoxPath
		{
			get
			{
				if(m_strFirefox != null) return m_strFirefox;
				else
				{
					try { m_strFirefox = FindFirefox(); }
					catch(Exception) { m_strFirefox = null; }

					return m_strFirefox;
				}
			}
		}

		public static string OperaPath
		{
			get
			{
				if(m_strOpera != null) return m_strOpera;
				else
				{
					try { m_strOpera = FindOpera(); }
					catch(Exception) { m_strOpera = null; }

					return m_strOpera;
				}
			}
		}

		public static string ChromePath
		{
			get
			{
				if(m_strChrome != null) return m_strChrome;
				else
				{
					try { m_strChrome = FindChrome(); }
					catch(Exception) { m_strChrome = null; }

					return m_strChrome;
				}
			}
		}

		public static string FillPlaceholders(string strText, SprContentFlags cf)
		{
			string str = strText;

			str = AppLocator.ReplacePath(str, @"{INTERNETEXPLORER}", AppLocator.InternetExplorerPath, cf);
			str = AppLocator.ReplacePath(str, @"{FIREFOX}", AppLocator.FirefoxPath, cf);
			str = AppLocator.ReplacePath(str, @"{OPERA}", AppLocator.OperaPath, cf);
			str = AppLocator.ReplacePath(str, @"{GOOGLECHROME}", AppLocator.ChromePath, cf);

			return str;
		}

		private static string ReplacePath(string str, string strPlaceholder,
			string strFill, SprContentFlags cf)
		{
			if(str == null) { Debug.Assert(false); return string.Empty; }
			if(strPlaceholder == null) { Debug.Assert(false); return str; }
			if(strPlaceholder.Length == 0) { Debug.Assert(false); return str; }
			if(strFill == null) return str; // No assert

			string strRep;
			if((cf != null) && cf.EncodeQuotesForCommandLine)
				strRep = "\"" + SprEngine.TransformContent(strFill, cf) + "\"";
			else
				strRep = SprEngine.TransformContent("\"" + strFill + "\"", cf);

			return StrUtil.ReplaceCaseInsensitive(str, strPlaceholder, strRep);
		}

		private static string FindInternetExplorer()
		{
			RegistryKey kApps = Registry.ClassesRoot.OpenSubKey("Applications", false);
			RegistryKey kIE = kApps.OpenSubKey("iexplore.exe", false);
			RegistryKey kShell = kIE.OpenSubKey("shell", false);
			RegistryKey kOpen = kShell.OpenSubKey("open", false);
			RegistryKey kCommand = kOpen.OpenSubKey("command", false);
			string strPath = (kCommand.GetValue(string.Empty) as string);

			if(strPath != null)
			{
				strPath = strPath.Trim();
				strPath = UrlUtil.GetQuotedAppPath(strPath).Trim();
			}
			else { Debug.Assert(false); }

			kCommand.Close();
			kOpen.Close();
			kShell.Close();
			kIE.Close();
			kApps.Close();
			return strPath;
		}

		private static string FindFirefox()
		{
			RegistryKey kSoftware = Registry.LocalMachine.OpenSubKey("SOFTWARE", false);
			RegistryKey kMozilla = kSoftware.OpenSubKey("Mozilla", false);
			RegistryKey kFirefox = kMozilla.OpenSubKey("Mozilla Firefox", false);

			string strCurVer = (kFirefox.GetValue("CurrentVersion") as string);
			if((strCurVer == null) || (strCurVer.Length == 0))
			{
				kFirefox.Close();
				kMozilla.Close();
				kSoftware.Close();
				return null;
			}

			RegistryKey kCurVer = kFirefox.OpenSubKey(strCurVer);
			RegistryKey kMain = kCurVer.OpenSubKey("Main");

			string strPath = (kMain.GetValue("PathToExe") as string);
			if(strPath != null)
			{
				strPath = strPath.Trim();
				strPath = UrlUtil.GetQuotedAppPath(strPath).Trim();
			}
			else { Debug.Assert(false); }

			kMain.Close();
			kCurVer.Close();
			kFirefox.Close();
			kMozilla.Close();
			kSoftware.Close();
			return strPath;
		}

		private static string FindOpera()
		{
			RegistryKey kHtml = Registry.ClassesRoot.OpenSubKey("Opera.HTML", false);
			RegistryKey kShell = kHtml.OpenSubKey("shell", false);
			RegistryKey kOpen = kShell.OpenSubKey("open", false);
			RegistryKey kCommand = kOpen.OpenSubKey("command", false);
			string strPath = (kCommand.GetValue(string.Empty) as string);

			if((strPath != null) && (strPath.Length > 0))
			{
				strPath = strPath.Trim();
				strPath = UrlUtil.GetQuotedAppPath(strPath).Trim();
			}
			else strPath = null;

			kCommand.Close();
			kOpen.Close();
			kShell.Close();
			kHtml.Close();
			return strPath;
		}

		// HKEY_CLASSES_ROOT\\Applications\\chrome.exe\\shell\\open\\command
		private static string FindChrome()
		{
			RegistryKey kApps = Registry.ClassesRoot.OpenSubKey("Applications", false);
			RegistryKey kExe = kApps.OpenSubKey("chrome.exe", false);
			RegistryKey kShell = kExe.OpenSubKey("shell", false);
			RegistryKey kOpen = kShell.OpenSubKey("open", false);
			RegistryKey kCommand = kOpen.OpenSubKey("command", false);
			string strPath = (kCommand.GetValue(string.Empty) as string);

			if((strPath != null) && (strPath.Length > 0))
			{
				strPath = strPath.Trim();
				strPath = UrlUtil.GetQuotedAppPath(strPath).Trim();
			}
			else strPath = null;

			kCommand.Close();
			kOpen.Close();
			kShell.Close();
			kExe.Close();
			kApps.Close();
			return strPath;
		}
	}
}
