/*
 * Apache License, Version 2.0
 * Copyright 2019-2021 NVIDIA Corporation
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nvidia.Clara.DicomAdapter.Configuration;
using System;
using System.IO.Abstractions;

namespace Nvidia.Clara.DicomAdapter.Server.Services.Disk
{
    public interface IStorageInfoProvider
    {
        bool HasSpaceAvailableToStore { get; }
        bool HasSpaceAvailableForExport { get; }
        bool HasSpaceAvailableToRetrieve { get; }
        long AvailableFreeSpace { get; }
    }

    public class StorageInfoProvider : IStorageInfoProvider
    {
        private const long OneGB = 1000000000;
        private readonly StorageConfiguration _storageConfiguration;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<StorageInfoProvider> _logger;
        private long _reservedSpace;

        public bool HasSpaceAvailableToStore { get => IsSpaceAvailable(); }

        public bool HasSpaceAvailableForExport { get => IsSpaceAvailable(); }

        public bool HasSpaceAvailableToRetrieve { get => IsSpaceAvailable(); }

        public long AvailableFreeSpace
        {
            get
            {
                var driveInfo = _fileSystem.DriveInfo.FromDriveName(_storageConfiguration.TemporaryDataDirFullPath);
                return driveInfo.AvailableFreeSpace;
            }
        }

        public StorageInfoProvider(
            IOptions<DicomAdapterConfiguration> dicomAdapterConfiguration,
            IFileSystem fileSystem,
            ILogger<StorageInfoProvider> logger)
        {
            if (dicomAdapterConfiguration is null)
            {
                throw new ArgumentNullException(nameof(dicomAdapterConfiguration));
            }

            _storageConfiguration = dicomAdapterConfiguration.Value.Storage;
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (!_fileSystem.Directory.Exists(_storageConfiguration.TemporaryDataDirFullPath))
            {
                _fileSystem.Directory.CreateDirectory(_storageConfiguration.TemporaryDataDirFullPath);
            }
            _logger.Log(LogLevel.Information, $"Temporary Stroage Path={_storageConfiguration.TemporaryDataDirFullPath}.");

            Initialize();
        }

        private void Initialize()
        {
            var driveInfo = _fileSystem.DriveInfo.FromDriveName(_storageConfiguration.TemporaryDataDirFullPath);
            _reservedSpace = (long)(driveInfo.TotalSize * (1 - (_storageConfiguration.Watermark / 100.0)));
            _reservedSpace = Math.Max(_reservedSpace, _storageConfiguration.ReserveSpaceGB * OneGB);
            _logger.Log(LogLevel.Information, $"Storage Size: {driveInfo.TotalSize:N0}. Reserved: {_reservedSpace:N0}.");
        }

        private bool IsSpaceAvailable()
        {
            var driveInfo = _fileSystem.DriveInfo.FromDriveName(_storageConfiguration.TemporaryDataDirFullPath);

            var freeSpace = driveInfo.AvailableFreeSpace;

            _logger.Log(LogLevel.Debug, $"Storage Size: {driveInfo.TotalSize:N0}. Reserved: {_reservedSpace:N0}. Available: {freeSpace:N0}.");
            return freeSpace > _reservedSpace;
        }
    }
}