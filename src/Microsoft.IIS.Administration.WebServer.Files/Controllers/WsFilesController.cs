﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


namespace Microsoft.IIS.Administration.WebServer.Files
{
    using Administration.Files;
    using AspNetCore.Mvc;
    using Core;
    using Core.Http;
    using Core.Utils;
    using Sites;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using Web.Administration;

    public class WsFilesController : ApiBaseController
    {
        private IFileProvider _fileService;

        public WsFilesController()
        {
            _fileService = FileProvider.Default;
        }

        [HttpDelete]
        [HttpPatch]
        [HttpPost]
        [HttpPut]
        public IActionResult NotAllowed()
        {
            return new StatusCodeResult((int)HttpStatusCode.MethodNotAllowed);
        }

        [HttpHead]
        [ResourceInfo(Name = Defines.FilesName)]
        public object Head()
        {
            Site site;
            FileId parentId;
            string nameFilter, pathFilter, physicalPath;

            PreGet(out parentId, out site, out nameFilter, out pathFilter);

            if (pathFilter != null) {
                return GetByPath(pathFilter == string.Empty ? "/" : pathFilter);
            }

            if (parentId == null || site == null) {
                return NotFound();
            }

            physicalPath = FilesHelper.GetPhysicalPath(site, parentId.Path);

            if (!_fileService.DirectoryExists(physicalPath)) {
                return NotFound();
            }
            
            var dirInfo = _fileService.GetDirectoryInfo(physicalPath);            
            var children = GetChildren(site, parentId.Path, dirInfo, nameFilter, false);

            // Set HTTP header for total count
            this.Context.Response.SetItemsCount(children.Count());

            return Ok();
        }

        [HttpGet]
        [ResourceInfo(Name = Defines.FilesName)]
        public object Get()
        {
            Site site;
            FileId parentId;
            string nameFilter, pathFilter, physicalPath;

            PreGet(out parentId, out site, out nameFilter, out pathFilter);

            if (pathFilter != null) {
                return GetByPath(pathFilter == string.Empty ? "/" : pathFilter);
            }

            if (parentId == null || site == null) {
                return NotFound();
            }

            physicalPath = FilesHelper.GetPhysicalPath(site, parentId.Path);

            if (!_fileService.DirectoryExists(physicalPath)) {
                return NotFound();
            }

            var fields = Context.Request.GetFields();
            var dirInfo = _fileService.GetDirectoryInfo(physicalPath);            
            var children = GetChildren(site, parentId.Path, dirInfo, nameFilter, true, fields);

            // Set HTTP header for total count
            this.Context.Response.SetItemsCount(children.Count());

            return new
            {
                files = children
            };
        }

        [HttpGet]
        [ResourceInfo(Name = Defines.FileName)]
        public object Get(string id)
        {
            FileId fileId = new FileId(id);
            Site site = SiteHelper.GetSite(fileId.SiteId);

            if (site == null) {
                return NotFound();
            }

            var physicalPath = FilesHelper.GetPhysicalPath(site, fileId.Path);
            
            if (!_fileService.FileExists(physicalPath) && !_fileService.DirectoryExists(physicalPath)) {
                return NotFound();
            }

            var fields = Context.Request.GetFields();

            return FilesHelper.ToJsonModel(site, fileId.Path, fields);
        }



        private void PreGet(out FileId parentId, out Site site, out string nameFilter, out string pathFilter)
        {
            string parentUuid = Context.Request.Query[Defines.PARENT_IDENTIFIER];
            nameFilter = Context.Request.Query["name"];
            pathFilter = Context.Request.Query["path"];

            if (!string.IsNullOrEmpty(nameFilter)) {
                if (nameFilter.IndexOfAny(PathUtil.InvalidFileNameChars) != -1) {
                    throw new ApiArgumentException("name");
                }
            }
            
            parentId = string.IsNullOrEmpty(parentUuid) ? null : new FileId(parentUuid);
            site = parentId == null ? null : SiteHelper.GetSite(parentId.SiteId);
        }

        private IEnumerable<object> GetChildren(Site site, string path, DirectoryInfo parent, string nameFilter, bool jsonModels = true, Fields fields = null)
        {
            var files = new Dictionary<string, object>();

            //
            // Virtual Directories
            foreach (var vdir in FilesHelper.GetVdirs(site, path)) {
                if (string.IsNullOrEmpty(nameFilter) || vdir.Name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) != -1) {

                    if (!files.ContainsKey(vdir.Path)) {
                        files.Add(vdir.Path, jsonModels ? FilesHelper.VdirToJsonModelRef(vdir, fields) : vdir);
                    }
                }
            }

            //
            // Directories
            foreach (var d in _fileService.GetDirectories(parent.FullName, string.IsNullOrEmpty(nameFilter) ? "*" : $"*{nameFilter}*")) {
                string p = Path.Combine(path, d.Name);

                if (!files.ContainsKey(p) && !d.Attributes.HasFlag(FileAttributes.Hidden) && !d.Attributes.HasFlag(FileAttributes.System)) {
                    files.Add(p, jsonModels ? FilesHelper.DirectoryToJsonModelRef(site, p, fields) : d);
                }
            }

            //
            // Files
            foreach (var f in _fileService.GetFiles(parent.FullName, string.IsNullOrEmpty(nameFilter) ? "*" : $"*{nameFilter}*")) {
                string p = Path.Combine(path, f.Name);

                if (!files.ContainsKey(p) && !f.Attributes.HasFlag(FileAttributes.Hidden) && !f.Attributes.HasFlag(FileAttributes.System)) {
                    files.Add(p, jsonModels ? FilesHelper.FileToJsonModelRef(site, p, fields) : f);
                }
            }

            return files.Values;
        }

        private object GetByPath(string path)
        {
            if (!FilesHelper.IsValidPath(path)) {
                throw new ApiArgumentException("path");
            }

            Site site = SiteHelper.ResolveSite();

            if (site == null) {
                return NotFound();
            }

            string physicalPath = FilesHelper.GetPhysicalPath(site, path);

            var fields = Context.Request.GetFields();
            var dirInfo = _fileService.GetDirectoryInfo(physicalPath);

            if (!_fileService.DirectoryExists(physicalPath) && !_fileService.FileExists(physicalPath)) {
                throw new NotFoundException("path");
            }

            return FilesHelper.ToJsonModel(site, path, fields);
        }
    }
}
