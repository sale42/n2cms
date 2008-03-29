﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Web.UI;

namespace N2.Web.UI.WebControls
{
	public class Ul : Control
	{
		private string cssClass;

		public string CssClass
		{
			get { return cssClass; }
			set { cssClass = value; }
		}

		protected override void Render(HtmlTextWriter writer)
		{
			if (string.IsNullOrEmpty(CssClass))
				writer.Write("<ul>");
			else
				writer.Write("<ul class=\"{0}\">", CssClass);
			RenderChildren(writer);
			writer.Write("</ul>");
		}
	}
}
