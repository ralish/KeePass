using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.IO;

using KeePass.Resources;

using KeePassLib;
using KeePassLib.Interfaces;
using KeePassLib.Serialization;

namespace KeePass.DataExchange.Formats
{
	public sealed class KeePassKdb2x : FormatImporter
	{
		public override string FormatName { get { return "KeePass KDB 2.x"; } }
		public override string DefaultExtension { get { return "kdb"; } }
		public override string AppGroup { get { return PwDefs.ShortProductName; } }

		public override bool SupportsUuids { get { return true; } }
		public override bool RequiresKey { get { return true; } }

		public override Image SmallIcon
		{
			get { return KeePass.Properties.Resources.B16x16_KeePass; }
		}

		public override void Import(PwDatabase pwStorage, Stream sInput,
			IStatusLogger slLogger)
		{
			Kdb4File kdb4 = new Kdb4File(pwStorage);
			FileOpenResult fr = kdb4.Load(sInput, Kdb4File.KdbFormat.Default, slLogger);

			if(fr.Code != FileOpenResultCode.Success)
				throw new FormatException(ResUtil.FileOpenResultToString(fr));
		}
	}
}
