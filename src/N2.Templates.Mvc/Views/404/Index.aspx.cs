using System;
using System.Web.UI;
using N2.Web;

namespace N2.Templates.Mvc.Views
{
	public class NotFound404 : Page
	{
		protected override void OnInit(EventArgs args)
		{
			try
			{
				N2.ContentItem page = Mvc.Find.StartPage.NotFoundPage;
				if (page != null)
				{
					var wc = N2.Context.Current.Resolve<N2.Web.IWebContext>();
					wc.CurrentPage = page;
					Server.Execute(Url.Parse(page.FindPath(PathData.DefaultAction).RewrittenUrl).AppendQuery("postback", page.Url));
					Response.Status = "404 Not Found";
					Response.End();
					return;
				}
			}
			catch
			{
			}
			Response.Status = "404 Not Found";
			Response.Write("<html><body><h1>404 Not Found</h1></body></html>");
			Response.End();
		}
	}
}