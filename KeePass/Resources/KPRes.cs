﻿/*
  KeePass Password Safe - The Open-Source Password Manager
  Copyright (C) 2003-2020 Dominik Reichl <dominik.reichl@t-online.de>

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
using System.Text;

using KeePass.App;

using KeePassLib.Resources;

namespace KeePass.Resources
{
	public static partial class KPRes
	{
		// For backward compatibility with plugins
		[Obsolete]
		public static string NewDatabaseFileName
		{
			get
			{
				return (KPRes.Database + "." + AppDefs.FileExtension.FileExt);
			}
		}

		// For backward compatibility with plugins
		[Obsolete]
		public static string CreateNewDatabase
		{
			get { return KPRes.CreateNewDatabase2; }
		}

		[Obsolete]
		public static string UnknownError
		{
			get { return KLRes.UnknownError; }
		}
	}
}
