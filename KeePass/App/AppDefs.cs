/*
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
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using KeePass.UI;

namespace KeePass.App
{
	public static class AppDefs
	{
		public enum ColumnID
		{
			Title,
			UserName,
			Password,
			Url,
			Notes,
			CreationTime,
			LastAccessTime,
			LastModificationTime,
			ExpiryTime,
			Uuid,
			Attachment,
			Count // Virtual identifier representing the number of columns
		}

		public static readonly Color ColorControlNormal = SystemColors.Window;
		public static readonly Color ColorControlDisabled = SystemColors.Control;
		public static readonly Color ColorEditError = Color.FromArgb(255, 192, 192);

		public const string XslFileHtmlLite = "XSL\\KDB4_DetailsLite.xsl";
		public const string XslFileHtmlFull = "XSL\\KDB4_DetailsFull.xsl";
		public const string XslFileHtmlTabular = "XSL\\KDB4_Tabular.xsl";

		public const string PluginProductName = "KeePass Plugin";

		// public const string MruNameValueSplitter = @"/::/";

		/// <summary>
		/// Hot key IDs (used in WM_HOTKEY window messages).
		/// </summary>
		public static class GlobalHotKeyID
		{
			public const int AutoType = 195;
			public const int ShowWindow = 226;
			public const int EntryMenu = 227;
		}

		public static class HelpTopics
		{
			public const string Acknowledgements = "base/credits";
			public const string License = "v2/license";

			public const string DatabaseSettings = "v2/dbsettings";
			public const string DbSettingsGeneral = "general";
			public const string DbSettingsSecurity = "security";
			public const string DbSettingsProtection = "protection";
			public const string DbSettingsCompression = "compression";

			public const string AutoType = "base/autotype";
			public const string AutoTypeObfuscation = "v2/autotype_obfuscation";

			public const string Entry = "v2/entry";
			public const string EntryGeneral = "general";
			public const string EntryStrings = "advanced";
			public const string EntryAutoType = "autotype";
			public const string EntryHistory = "history";

			public const string KeySources = "base/keys";
			public const string PwGenerator = "base/pwgenerator";
			public const string IOConnections = "v2/ioconnect";

			public const string ImportExport = "base/importexport";
			public const string ImportExportSteganos = "imp_steganos";

			public const string AppPolicy = "v2/policy";
		}

		public static class CommandLineOptions
		{
			public const string Password = "pw";
			public const string KeyFile = "keyfile";
			public const string UserAccount = "useraccount";

			public const string PreSelect = "preselect";

			public const string FileExtRegister = "registerfileext";
			public const string FileExtUnregister = "unregisterfileext";

			public const string ExitAll = "exit-all";
		}

		public static class FileExtension
		{
			public const string FileExt = "kdbx";
			public const string ExtID = "kdbfile";

			public const string KeyFile = "key";
		}

		public const string AutoRunName = "KeePass Password Safe";

		public const string MutexName = "KeePassAppMutex";
		public const string MutexNameGlobal = "KeePassAppMutexEx";

		public const string ScriptExtension = "kps";

		public const int InvalidWindowValue = -16381;

		public static class NamedEntryColor
		{
			public static readonly Color LightRed = Color.FromArgb(255, 204, 204);
			public static readonly Color LightGreen = Color.FromArgb(204, 255, 204);
			public static readonly Color LightBlue = Color.FromArgb(153, 204, 255);
			public static readonly Color LightYellow = Color.FromArgb(255, 255, 153);
		}

		public const string DefaultTrlAuthor = "Dominik Reichl";
		public const string DefaultTrlContact = @"http://www.dominik-reichl.de/";

		public const string LanguageInfoFileName = "LanguageInfo.xml";

		public const string ColumnIdnCreationTime = "CreationTime";
		public const string ColumnIdnLastAccessTime = "LastAccessTime";
		public const string ColumnIdnLastModificationTime = "LastModificationTime";
		public const string ColumnIdnExpiryTime = "ExpiryTime";
		public const string ColumnIdnUuid = "UUID";
		public const string ColumnIdnAttachment = "Attachment";

		public const string DefaultLanguage = "en"; // ^= string.Empty
	}
}
