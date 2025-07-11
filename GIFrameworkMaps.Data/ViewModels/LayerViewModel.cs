﻿using GIFrameworkMaps.Data.Models;

namespace GIFrameworkMaps.Data.ViewModels
{
	public class LayerViewModel
	{
		public int Id { get; set; }
		public bool IsDefault { get; set; }
		public string? Name { get; set; }
		public int SortOrder { get; set; }
		public int? MinZoom { get; set; }
		public int? MaxZoom { get; set; }
		public Bound? Bound { get; set; }
		public int ZIndex { get; set; }
		public int DefaultOpacity { get; set; }
		public int DefaultSaturation { get; set; }
		public bool Queryable { get; set; }
		public string? InfoTemplate { get; set; }
		public string? InfoListTitleTemplate { get; set; }
		public bool Filterable { get; set; }
		public bool DefaultFilterEditable { get; set; }
		public bool ProxyMetaRequests { get; set; }
		public bool ProxyMapRequests { get; set; }
		public int? RefreshInterval { get; set; }
		public LayerSource? LayerSource { get; set; }
		public LayerDisclaimer? LayerDisclaimer { get; set; }
	}
}
