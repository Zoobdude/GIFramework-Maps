﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace GIFrameworkMaps.Data.Models
{
    /// <summary>
    /// Defines a single source of data and related options
    /// </summary>
    public class LayerSource
    {
        public LayerSource()
        {
            LayerSourceOptions = new List<LayerSourceOption>();
        }

        public int Id { get; set; }
        [MaxLength(100)]
        public string Name { get; set; }
        public string Description { get; set; }
        public Attribution Attribution { get; set; }
        public LayerSourceType LayerSourceType { get; set; }
        public List<LayerSourceOption> LayerSourceOptions { get; set; } 
    }
}