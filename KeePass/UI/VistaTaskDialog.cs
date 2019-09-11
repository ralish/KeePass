﻿/*
  KeePass Password Safe - The Open-Source Password Manager
  Copyright (C) 2003-2011 Dominik Reichl <dominik.reichl@t-online.de>

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
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Diagnostics;
using System.Drawing;

using KeePass.Native;

using KeePassLib.Utility;

namespace KeePass.UI
{
	[Flags]
	public enum VtdFlags
	{
		None = 0,
		EnableHyperlinks = 0x0001,
		UseHIconMain = 0x0002,
		UseHIconFooter = 0x0004,
		AllowDialogCancellation = 0x0008,
		UseCommandLinks = 0x0010,
		UseCommandLinksNoIcon = 0x0020,
		ExpandFooterArea = 0x0040,
		ExpandedByDefault = 0x0080,
		VerificationFlagChecked = 0x0100,
		ShowProgressBar = 0x0200,
		ShowMarqueeProgressBar = 0x0400,
		CallbackTimer = 0x0800,
		PositionRelativeToWindow = 0x1000,
		RtlLayout = 0x2000,
		NoDefaultRadioButton = 0x4000
	}

	[Flags]
	public enum VtdCommonButtonFlags
	{
		None = 0,
		OkButton = 0x0001, // Return value: IDOK
		YesButton = 0x0002, // Return value: IDYES
		NoButton = 0x0004, // Return value: IDNO
		CancelButton = 0x0008, // Return value: IDCANCEL
		RetryButton = 0x0010, // Return value: IDRETRY
		CloseButton = 0x0020  // Return value: IDCLOSE
	}

	public enum VtdIcon
	{
		None = 0,
		Warning = 0xFFFF,
		Error = 0xFFFE,
		Information = 0xFFFD,
		Shield = 0xFFFC
	}

	public enum VtdCustomIcon
	{
		None = 0,
		Question = 1
	}

	// Pack = 4 required for 64-bit compatibility
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto, Pack = 4)]
	internal struct VtdButton
	{
		public int ID;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string Text;

		public VtdButton(bool bConstruct)
		{
			Debug.Assert(bConstruct);

			this.ID = (int)DialogResult.Cancel;
			this.Text = string.Empty;
		}
	}

	// Pack = 4 required for 64-bit compatibility
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto, Pack = 4)]
	internal struct VtdConfig
	{
		public uint cbSize;
		public IntPtr hwndParent;
		public IntPtr hInstance;
		
		[MarshalAs(UnmanagedType.U4)]
		public VtdFlags dwFlags;

		[MarshalAs(UnmanagedType.U4)]
		public VtdCommonButtonFlags dwCommonButtons;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string pszWindowTitle;

		public IntPtr hMainIcon;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string pszMainInstruction;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string pszContent;

		public uint cButtons;
		public IntPtr pButtons;
		public int nDefaultButton;
		public uint cRadioButtons;
		public IntPtr pRadioButtons;
		public int nDefaultRadioButton;
		
		[MarshalAs(UnmanagedType.LPWStr)]
		public string pszVerificationText;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string pszExpandedInformation;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string pszExpandedControlText;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string pszCollapsedControlText;

		public IntPtr hFooterIcon;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string pszFooter;

		public IntPtr pfCallback; // PFTASKDIALOGCALLBACK
		public IntPtr lpCallbackData;
		public uint cxWidth;

		public VtdConfig(bool bConstruct)
		{
			Debug.Assert(bConstruct);

			cbSize = (uint)Marshal.SizeOf(typeof(VtdConfig));
			hwndParent = IntPtr.Zero;
			hInstance = IntPtr.Zero;

			dwFlags = VtdFlags.None;
			if(Program.Translation.Properties.RightToLeft) dwFlags |= VtdFlags.RtlLayout;

			dwCommonButtons = VtdCommonButtonFlags.None;
			pszWindowTitle = null;
			hMainIcon = IntPtr.Zero;
			pszMainInstruction = string.Empty;
			pszContent = string.Empty;
			cButtons = 0;
			pButtons = IntPtr.Zero;
			nDefaultButton = 0;
			cRadioButtons = 0;
			pRadioButtons = IntPtr.Zero;
			nDefaultRadioButton = 0;
			pszVerificationText = null;
			pszExpandedInformation = null;
			pszExpandedControlText = null;
			pszCollapsedControlText = null;
			hFooterIcon = IntPtr.Zero;
			pszFooter = null;
			pfCallback = IntPtr.Zero; // PFTASKDIALOGCALLBACK
			lpCallbackData = IntPtr.Zero;
			cxWidth = 0;
		}
	}

	public sealed class VistaTaskDialog
	{
		private const int VtdConfigSize = 96;

		private VtdConfig m_cfg = new VtdConfig(true);
		private int m_iResult = (int)DialogResult.Cancel;
		private bool m_bVerification = false;

		private List<VtdButton> m_vButtons = new List<VtdButton>();

		public string WindowTitle
		{
			get { return m_cfg.pszWindowTitle; }
			set { m_cfg.pszWindowTitle = value; }
		}

		public string MainInstruction
		{
			get { return m_cfg.pszMainInstruction; }
			set { m_cfg.pszMainInstruction = value; }
		}

		public string Content
		{
			get { return m_cfg.pszContent; }
			set { m_cfg.pszContent = value; }
		}

		public bool CommandLinks
		{
			get { return ((m_cfg.dwFlags & VtdFlags.UseCommandLinks) != VtdFlags.None); }
			set
			{
				if(value) m_cfg.dwFlags |= VtdFlags.UseCommandLinks;
				else m_cfg.dwFlags &= ~VtdFlags.UseCommandLinks;
			}
		}

		public string VerificationText
		{
			get { return m_cfg.pszVerificationText; }
			set { m_cfg.pszVerificationText = value; }
		}

		public int Result
		{
			get { return m_iResult; }
		}

		public bool ResultVerificationChecked
		{
			get { return m_bVerification; }
		}

		public VistaTaskDialog(IntPtr hParent)
		{
			m_cfg.hwndParent = hParent;
		}

		public void AddButton(int iResult, string strCommand, string strDescription)
		{
			if(strCommand == null) throw new ArgumentNullException("strCommand");

			VtdButton btn = new VtdButton(true);

			if(strDescription == null) btn.Text = strCommand;
			else btn.Text = strCommand + "\n" + strDescription;

			btn.ID = iResult;

			m_vButtons.Add(btn);
		}

		public void SetIcon(VtdIcon vtdIcon)
		{
			m_cfg.dwFlags &= ~VtdFlags.UseHIconMain;
			m_cfg.hMainIcon = new IntPtr((int)vtdIcon);
		}

		public void SetIcon(VtdCustomIcon vtdIcon)
		{
			if(vtdIcon == VtdCustomIcon.Question)
				this.SetIcon(SystemIcons.Question.Handle);
		}

		public void SetIcon(IntPtr hIcon)
		{
			m_cfg.dwFlags |= VtdFlags.UseHIconMain;
			m_cfg.hMainIcon = hIcon;
		}

		private void ButtonsToPtr()
		{
			if(m_vButtons.Count == 0) { m_cfg.pButtons = IntPtr.Zero; return; }

			int nConfigSize = Marshal.SizeOf(typeof(VtdButton));
			m_cfg.pButtons = Marshal.AllocHGlobal(m_vButtons.Count * nConfigSize);
			m_cfg.cButtons = (uint)m_vButtons.Count;

			for(int i = 0; i < m_vButtons.Count; ++i)
			{
				long l = m_cfg.pButtons.ToInt64() + (i * nConfigSize);
				Marshal.StructureToPtr(m_vButtons[i], new IntPtr(l), false);
			}
		}

		private void FreeButtonsPtr()
		{
			if(m_cfg.pButtons == IntPtr.Zero) return;

			int nConfigSize = Marshal.SizeOf(typeof(VtdButton));
			for(int i = 0; i < m_vButtons.Count; ++i)
			{
				long l = m_cfg.pButtons.ToInt64() + (i * nConfigSize);
				Marshal.DestroyStructure(new IntPtr(l), typeof(VtdButton));
			}

			Marshal.FreeHGlobal(m_cfg.pButtons);
			m_cfg.pButtons = IntPtr.Zero;
			m_cfg.cButtons = 0;
		}

		public bool ShowDialog()
		{
			MessageService.ExternalIncrementMessageCount();
			bool bResult = InternalShowDialog();
			MessageService.ExternalDecrementMessageCount();
			return bResult;
		}

		private bool InternalShowDialog()
		{
			if(IntPtr.Size == 4)
				{ Debug.Assert(Marshal.SizeOf(typeof(VtdConfig)) == VtdConfigSize); }

			m_cfg.cbSize = (uint)Marshal.SizeOf(typeof(VtdConfig));

			int pnButton = 0, pnRadioButton = 0;
			bool bVerification = false;

			try { ButtonsToPtr(); }
			catch(Exception) { Debug.Assert(false); return false; }

			try
			{
				if(NativeMethods.TaskDialogIndirect(ref m_cfg, out pnButton,
					out pnRadioButton, out bVerification) != 0)
					throw new NotSupportedException();
			}
			catch(Exception) { return false; }
			finally
			{
				try { FreeButtonsPtr(); }
				catch(Exception) { Debug.Assert(false); }
			}

			m_iResult = pnButton;
			m_bVerification = bVerification;
			return true;
		}

		public static bool ShowMessageBox(string strContent, string strMainInstruction,
			string strWindowTitle, VtdIcon vtdIcon, IntPtr hParent)
		{
			VistaTaskDialog vtd = new VistaTaskDialog(hParent);

			vtd.CommandLinks = false;

			if(strContent != null) vtd.Content = strContent;
			if(strMainInstruction != null) vtd.MainInstruction = strMainInstruction;
			if(strWindowTitle != null) vtd.WindowTitle = strWindowTitle;

			vtd.SetIcon(vtdIcon);

			return vtd.ShowDialog();
		}
	}
}
