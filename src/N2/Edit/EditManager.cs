using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Web;
using System.Web.UI;
using N2.Definitions;
using N2.Persistence;
using N2.Web;
using N2.Web.UI;
using N2.Web.UI.WebControls;
using N2.Collections;
using N2.Edit.Settings;
using N2.Security;
using N2.Configuration;
using N2.Plugin;

namespace N2.Edit
{
	/// <summary>
	/// Class responsible for plugins in edit mode, knowling links to edit 
	/// pages and saving interaction.
	/// </summary>
	public class EditManager : IEditManager
	{
		protected System.ComponentModel.EventHandlerList Events = new System.ComponentModel.EventHandlerList();
		protected static readonly object savingVersionKey = new object();
		protected static readonly object addedEditorKey = new object();
		private IList<string> uploadFolders = new List<string>();

		private readonly IDefinitionManager definitions;
		private readonly IPersister persister;
		private readonly IVersionManager versioner;
		private readonly NavigationSettings settings;
        private readonly IPluginFinder pluginFinder;
        private readonly ISecurityManager securityManager;
		private string editTreeUrl = "~/edit/Navigation/Tree.aspx";
		private string editTreeUrlFormat = "{1}?selected={0}";
		private string editPreviewUrlFormat = "{0}";
        private string editItemUrl = "~/edit/edit.aspx";
        private string editInterfaceUrl = "~/edit/";
        private string newItemUrl = "~/edit/new.aspx";
        private string deleteItemUrl = "~/edit/delete.aspx";
        private bool enableVersioning = true;
		
		public EditManager(IDefinitionManager definitions, IPersister persister, IVersionManager versioner, ISecurityManager securityManager, IPluginFinder pluginFinder, NavigationSettings settings)
		{
			this.definitions = definitions;
			this.persister = persister;
			this.versioner = versioner;
			this.settings = settings;
			this.securityManager = securityManager;
            this.pluginFinder = pluginFinder;
		}

        public EditManager(IDefinitionManager definitions, IPersister persister, IVersionManager versioner, ISecurityManager securityManager, IPluginFinder pluginFinder, NavigationSettings settings, EditSection config)
            : this(definitions, persister, versioner, securityManager, pluginFinder, settings)
        {
            EditTreeUrl = config.EditTreeUrl;
            EditPreviewUrlFormat = config.EditPreviewUrlFormat;
            EditItemUrl = config.EditItemUrl;
            EditInterfaceUrl = config.EditInterfaceUrl;
            NewItemUrl = config.NewItemUrl;
            DeleteItemUrl = config.DeleteItemUrl;
            EnableVersioning = config.Versions.Enabled;
			MaximumNumberOfVersions = config.Versions.MaximumPerItem;
			uploadFolders = new List<string>(config.UploadFolders.Folders);
        }

        public string EditInterfaceUrl
        {
            get { return editInterfaceUrl; }
            set { editInterfaceUrl = value; }
        }

        public string EditTreeUrl
        {
            get { return editTreeUrl; }
            set { editTreeUrl = value; }
        }

        public string DeleteItemUrl
        {
            get { return deleteItemUrl; }
            set { deleteItemUrl = value; }
        }

        public string NewItemUrl
        {
            get { return newItemUrl; }
            set { newItemUrl = value; }
        }

        public string EditItemUrl
        {
            get { return editItemUrl; }
            set { editItemUrl = value; }
        }

        /// <summary>Gets an alternative tree url format when edit mode is displayed.</summary>
		/// <remarks>Accepted format value is {0} for url encoded selected item.</remarks>
		public string EditTreeUrlFormat
		{
			get { return editTreeUrlFormat; }
			set { editTreeUrlFormat = value; }
		}

		/// <summary>Gets an alternative preview url format displayed when edit page is loaded.</summary>
		/// <remarks>Accepted format values are {0} for selected page and {1} for url encoded selected item.</remarks>
		public string EditPreviewUrlFormat
		{
			get { return editPreviewUrlFormat; }
			set { editPreviewUrlFormat = value; }
		}

		/// <summary>Gets or sets wether a version is saved when updating items.</summary>
		public bool EnableVersioning
		{
			get { return enableVersioning; }
			set { enableVersioning = value; }
		}

        /// <summary>Gets folders paths on the server where users are allowed to upload content through the interface.</summary>
        public IList<string> UploadFolders
	    {
	        get { return uploadFolders; }
	    }

		/// <summary>Number of item versions to keep.</summary>
		public int MaximumNumberOfVersions { get; set; }



	    /// <summary>Gets the url for the navigation frame.</summary>
		/// <param name="selectedItem">The currently selected item.</param>
		/// <returns>An url.</returns>
		public string GetNavigationUrl(INode selectedItem)
		{
            if(selectedItem == null)
                return null;

			return Url.ToAbsolute(string.Format(EditTreeUrlFormat, selectedItem.Path, EditTreeUrl));
		}

		/// <summary>Gets the url for the preview frame.</summary>
		/// <param name="selectedItem">The currently selected item.</param>
		/// <returns>An url.</returns>
		public string GetPreviewUrl(INode selectedItem)
		{
			string url = string.Format(EditPreviewUrlFormat,
				selectedItem.PreviewUrl,
				HttpUtility.UrlEncode(selectedItem.PreviewUrl)
				);
			return Url.ToAbsolute(url);
		}

		/// <summary>Adds defined editors and containers to a control.</summary>
		/// <param name="itemType">The type of content item whose editors to add.</param>
		/// <param name="editorContainer">The container onto which add the editors.</param>
		/// <param name="user">The user whose credentials will be queried.</param>
		public virtual IDictionary<string, Control> AddEditors(Type itemType, Control editorContainer, IPrincipal user)
		{
			ItemDefinition definition = definitions.GetDefinition(itemType);
			IEditableContainer rootContainer = definition.RootContainer;
			IDictionary<string, Control> addedEditors = new Dictionary<string, Control>();
			AddEditorsRecursive(rootContainer, editorContainer, user, addedEditors);
			return addedEditors;
		}

		#region Helpers
		/// <summary>Adds editors and containers to the supplied container.</summary>
		/// <param name="containerControl">The control on which editors and containres will be added.</param>
		/// <param name="contained">The definition that will add a control in the container.</param>
		public virtual void AddEditorsRecursive(IContainable contained, Control containerControl, IPrincipal user, IDictionary<string, Control> addedEditors)
		{
			Control added = contained.AddTo(containerControl);

			if (contained is Definitions.IEditable)
			{
				addedEditors[contained.Name] = added;
				OnAddedEditor(new ControlEventArgs(added));
			}
			if (contained is Definitions.IEditableContainer)
			{
				foreach (Definitions.IContainable subContained in ((Definitions.IEditableContainer)contained).GetContained(user))
				{
					AddEditorsRecursive(subContained, added, user, addedEditors);
				}
			}
		}
		#endregion

		/// <summary>Sets initial editor values.</summary>
		/// <param name="addedEditors">Previously added editor controls.</param>
		/// <param name="item">The content item to use for update.</param>
		/// <param name="user">The current user.</param>
		public virtual void UpdateEditors(ContentItem item, IDictionary<string, Control> addedEditors, IPrincipal user)
		{
			if (item == null) throw new ArgumentNullException("item");
			if (addedEditors == null) throw new ArgumentNullException("addedEditors");

			ItemDefinition definition = definitions.GetDefinition(item.GetType());
			ApplyModifications(definition, addedEditors);
			foreach (IEditable e in definition.GetEditables(user))
			{
                if(addedEditors.ContainsKey(e.Name) && addedEditors[e.Name] != null)
				    e.UpdateEditor(item, addedEditors[e.Name]);
			}
		}

		#region Helper
		/// <summary>Applies defined modifications to the editors.</summary>
		public virtual void ApplyModifications(ItemDefinition definition, IDictionary<string, Control> addedEditors)
		{
			foreach (string name in addedEditors.Keys)
			{
				foreach (N2.Web.UI.EditorModifierAttribute em in definition.GetModifiers(name))
				{
					Control editor = addedEditors[em.Name];
					em.Modify(editor);
				}
			}
		} 
		#endregion

		/// <summary>Updates the item by way of letting the defined editable attributes interpret the added editors.</summary>
		/// <param name="item">The item to update.</param>
		/// <param name="addedEditors">The previously added editors.</param>
		/// <param name="user">The user for filtering updatable editors.</param>
		/// <returns>Whether any property on the item was updated.</returns>
		public bool UpdateItem(ContentItem item, IDictionary<string, Control> addedEditors, IPrincipal user)
		{
			if (item == null) throw new ArgumentNullException("item");
			if (addedEditors == null) throw new ArgumentNullException("addedEditors");

			bool updated = false;
			ItemDefinition definition = definitions.GetDefinition(item.GetType());
			foreach (IEditable e in definition.GetEditables(user))
			{
				if(addedEditors.ContainsKey(e.Name))
				{
					updated = e.UpdateItem(item, addedEditors[e.Name]) || updated;
				}
			}

			if (updated)
			{
				item.Updated = Utility.CurrentTime();
				if (user != null)
					item.SavedBy = user.Identity.Name;
			}

			return updated;
		}
		
		/// <summary>
		/// Event that is triggered when page is saved/published
		/// </summary>
		public event EventHandler<ItemEventArgs> ItemSaved;

		protected virtual void OnItemSaved(ItemEventArgs e)
		{
			if (ItemSaved != null)
				ItemSaved(this, e);
		}

		/// <summary>Saves an item using values from the supplied item editor.</summary>
		/// <param name="item">The item to update.</param>
		/// <param name="addedEditors">The editors to update the item with.</param>
		/// <param name="versioningMode">How to treat the item beeing saved in respect to versioning.</param>
		/// <param name="user">The user that is performing the saving.</param>
		public virtual ContentItem Save(ContentItem item, IDictionary<string, Control> addedEditors, ItemEditorVersioningMode versioningMode, IPrincipal user)
		{
			// when an unpublished version is saved and published
			if(versioningMode == ItemEditorVersioningMode.SaveAsMaster)
			{
				return SaveAsMaster(item, addedEditors, user);
			}

			// when an item is saved without any new version
			if (versioningMode == ItemEditorVersioningMode.SaveOnly)
			{
				return SaveOnly(item, addedEditors, user);
			}
			
			// when an item is saved but a version is stored before the item is updated
			if (versioningMode == ItemEditorVersioningMode.VersionAndSave)
			{
				return VersionAndSave(item, addedEditors, user);
			}
				
			// when making a version without publishing the item
			if (versioningMode == ItemEditorVersioningMode.VersionOnly)
			{
				return VersionOnly(item, addedEditors, user);
			}
				
			throw new ArgumentException("Unexpected versioning mode.", "versioningMode");
		}

		ContentItem SaveAsMaster(ContentItem item, IDictionary<string, Control> addedEditors, IPrincipal user)
		{
			using (ITransaction tx = persister.Repository.BeginTransaction())
			{
				ContentItem itemToUpdate = item.VersionOf;
				if (itemToUpdate == null) throw new ArgumentException("Expected the current item to be a version of another item.", "item");

				if (ShouldStoreVersion(item))
					SaveVersion(itemToUpdate);

				DateTime? published = itemToUpdate.Published;
				bool wasUpdated = UpdateItem(itemToUpdate, addedEditors, user);
				if (wasUpdated || IsNew(itemToUpdate))
				{
					itemToUpdate.Published = published ?? Utility.CurrentTime();
					persister.Save(itemToUpdate);
				}

				tx.Commit();

				OnItemSaved(new ItemEventArgs(itemToUpdate));
				return item.VersionOf;
			}
		}

		ContentItem SaveOnly(ContentItem item, IDictionary<string, Control> addedEditors, IPrincipal user)
		{
			bool wasUpdated = UpdateItem(item, addedEditors, user);
			if (wasUpdated || IsNew(item))
				persister.Save(item);

			OnItemSaved(new ItemEventArgs(item));
			return item;
		}

		ContentItem VersionAndSave(ContentItem item, IDictionary<string, Control> addedEditors, IPrincipal user)
		{
			using (ITransaction tx = persister.Repository.BeginTransaction())
			{
				if (ShouldStoreVersion(item))
					SaveVersion(item);

				DateTime? initialPublished = item.Published;
				bool wasUpdated = UpdateItem(item, addedEditors, user);
				DateTime? updatedPublished = item.Published;

				// the item was the only version of an unpublished item - publish it
				if (initialPublished == null && updatedPublished == null)
				{
					item.Published = Utility.CurrentTime();
					wasUpdated = true;
				}

				if (wasUpdated || IsNew(item))
					persister.Save(item);

				tx.Commit();

				OnItemSaved(new ItemEventArgs(item));
				return item;
			}
		}

		ContentItem VersionOnly(ContentItem item, IDictionary<string, Control> addedEditors, IPrincipal user)
		{
			using (ITransaction tx = persister.Repository.BeginTransaction())
			{
				if (ShouldStoreVersion(item))
					item = SaveVersion(item);

				bool wasUpdated = UpdateItem(item, addedEditors, user);
				if (wasUpdated || IsNew(item))
				{
					item.Published = null;
					persister.Save(item);
				}

				tx.Commit();

				OnItemSaved(new ItemEventArgs(item));
				return item;
			}
		}

		private bool ShouldStoreVersion(ContentItem item)
        {
            return EnableVersioning && !IsNew(item) && item.GetType().GetCustomAttributes(typeof(Persistence.NotVersionableAttribute), true).Length == 0;
        }
        /// <summary>Gets the url to the edit interface.</summary>
        /// <returns>The url to the edit interface.</returns>
        public string GetEditInterfaceUrl()
        {
            return N2.Web.Url.ToAbsolute(EditInterfaceUrl);
        }

		/// <summary>Gets the url to the edit interface.</summary>
		/// <param name="selectedItem">The item to select in edit mode.</param>
		/// <returns>The url to the edit interface.</returns>
		public string GetEditInterfaceUrl(ContentItem selectedItem)
		{
            return FormatSelectedUrl(selectedItem, EditInterfaceUrl);
		}

		/// <summary>Gets the url to the select type of item to create.</summary>
		/// <param name="selectedItem">The currently selected item.</param>
		/// <returns>The url to the select new item to create page.</returns>
		public string GetSelectNewItemUrl(ContentItem selectedItem)
		{
			return FormatSelectedUrl(selectedItem, NewItemUrl);
		}

		/// <summary>Gets the url to the select type of item to create.</summary>
		/// <param name="selectedItem">The currently selected item.</param>
		/// <returns>The url to the select new item to create page.</returns>
		public string GetSelectNewItemUrl(ContentItem selectedItem, string zoneName)
		{
			return FormatSelectedUrl(selectedItem, NewItemUrl + "?zoneName=" + zoneName);
		}

		/// <summary>Gets the url to the delete item page.</summary>
		/// <param name="selectedItem">The currently selected item.</param>
		/// <returns>The url to the delete page.</returns>
		public string GetDeleteUrl(ContentItem selectedItem)
		{
			return FormatSelectedUrl(selectedItem, DeleteItemUrl);
		}

		private string FormatSelectedUrl(ContentItem selectedItem, string path)
		{
			Url url = Url.ToAbsolute(path);
            if (selectedItem != null)
                url = url.AppendQuery("selected=" + selectedItem.Path);
		    return url;
		}

		#region Helper Methods
		private ContentItem SaveVersion(ContentItem current)
		{
			ContentItem savedVersion = null;
			var handler = Events[savingVersionKey] as EventHandler<CancellableItemEventArgs>;
			Utility.InvokeEvent(handler, current, this, delegate(ContentItem item)
				{
					savedVersion = versioner.SaveVersion(item);
					versioner.TrimVersionCountTo(item, MaximumNumberOfVersions);
				});
			return savedVersion;
		}

		private bool IsNew(ContentItem current)
		{
			return current.ID == 0;
		}

		/// <summary>
		/// Triggers the AddedEditor event.
		/// </summary>
		private void OnAddedEditor(ControlEventArgs args)
		{
			EventHandler<ControlEventArgs> handler = Events[addedEditorKey] as EventHandler<ControlEventArgs>;
			if (handler != null)
				handler.Invoke(this, args);
		}

		#endregion



	    /// <summary>Occurs when a detail editor (a control that contains an editor) is added.</summary>
		public event EventHandler<ControlEventArgs> AddedEditor
		{
			add { Events.AddHandler(addedEditorKey, value); }
			remove { Events.RemoveHandler(addedEditorKey, value); }
		}

		/// <summary>Occurs when a version is about to be saved.</summary>
		public event EventHandler<CancellableItemEventArgs> SavingVersion
		{
			add { Events.AddHandler(savingVersionKey, value); }
			remove { Events.RemoveHandler(savingVersionKey, value); }
		}


		/// <summary>Gets the url to edit page creating new items.</summary>
		/// <param name="selected">The selected item.</param>
        /// <param name="definition">The type of item to edit.</param>
		/// <param name="zoneName">The zone to add the item to.</param>
		/// <param name="position">The position relative to the selected item to add the item.</param>
		/// <returns>The url to the edit page.</returns>
		public string GetEditNewPageUrl(ContentItem selected, ItemDefinition definition, string zoneName, CreationPosition position)
		{
			if (selected == null) throw new ArgumentNullException("selected");
			if (definition == null) throw new ArgumentNullException("definition");

			ContentItem parent = (position != CreationPosition.Below)
			                     	? selected.Parent
			                     	: selected;

			if (selected == null)
				throw new N2Exception("Cannot insert item before or after the root page.");

            N2.Web.Url url = EditItemUrl;
            url = url.AppendQuery("selected", parent.Path);
            url = url.AppendQuery("discriminator", definition.Discriminator);
            url = url.AppendQuery("zoneName", zoneName);

            if (position == CreationPosition.Before)
                url = url.AppendQuery("before", selected.Path);
            else if (position == CreationPosition.After)
                url = url.AppendQuery("after", selected.Path);
			return url;
		}

		/// <summary>Gets the url to the edit page where to edit an existing item.</summary>
		/// <param name="item">The item to edit.</param>
		/// <returns>The url to the edit page</returns>
		public string GetEditExistingItemUrl(ContentItem item)
		{
            if(item == null)
                return null;
			
            if(item.VersionOf != null)
				return string.Format("{0}?selectedUrl={1}", EditItemUrl, HttpUtility.UrlEncode(item.FindPath(PathData.DefaultAction).RewrittenUrl)); 
			
			return string.Format("{0}?selected={1}", EditItemUrl, item.Path);
		}


		public IEnumerable<T> GetPlugins<T>(IPrincipal user)
			where T: AdministrativePluginAttribute
		{
            return pluginFinder.GetPlugins<T>(user);
		}

		public ItemFilter GetEditorFilter(IPrincipal user)
		{
			ItemFilter filter = new AccessFilter(user, securityManager);
			if (!settings.DisplayDataItems)
			{
				filter = new CompositeFilter(new PageFilter(), filter);
			}
			return filter;
		}
	}
}
