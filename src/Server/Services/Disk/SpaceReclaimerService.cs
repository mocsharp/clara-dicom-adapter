﻿/*
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

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nvidia.Clara.DicomAdapter.API;
using Nvidia.Clara.DicomAdapter.API.Rest;
using Nvidia.Clara.DicomAdapter.Configuration;
using Polly;
using System;
using System.IO;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;

namespace Nvidia.Clara.DicomAdapter.Server.Services.Disk
{
    public class SpaceReclaimerService : IHostedService, IClaraService
    {
        private readonly ILogger<SpaceReclaimerService> _logger;
        private readonly IInstanceCleanupQueue _taskQueue;
        private readonly IFileSystem _fileSystem;
        private readonly string _payloadDirectory;

        public ServiceStatus Status { get; set; } = ServiceStatus.Unknown;

        public SpaceReclaimerService(
            IInstanceCleanupQueue taskQueue,
            ILogger<SpaceReclaimerService> logger,
            IOptions<DicomAdapterConfiguration> dicomAdapterConfiguration,
            IFileSystem fileSystem)
        {
            if (dicomAdapterConfiguration is null)
            {
                throw new ArgumentNullException(nameof(dicomAdapterConfiguration));
            }

            _taskQueue = taskQueue ?? throw new ArgumentNullException(nameof(taskQueue));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _payloadDirectory = dicomAdapterConfiguration.Value.Storage.TemporaryDataDirFullPath;
        }

        private void BackgroundProcessing(CancellationToken stoppingToken)
        {
            _logger.Log(LogLevel.Information, "Disk Space Reclaimer Hosted Service is running.");
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.Log(LogLevel.Debug, "Waiting for instance...");
                var filePath = _taskQueue.Dequeue(stoppingToken);

                if (filePath is null) continue; // likely canceled

                Policy.Handle<Exception>()
                    .WaitAndRetry(
                        3,
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        (exception, retryCount, context) =>
                        {
                            _logger.Log(LogLevel.Error, exception, $"Error occurred deleting file {filePath} on {retryCount} retry.");
                        })
                    .Execute(() =>
                    {
                        _logger.Log(LogLevel.Debug, "Deleting file {0}", filePath);
                        if (_fileSystem.File.Exists(filePath))
                        {
                            _fileSystem.File.Delete(filePath);
                            _logger.Log(LogLevel.Debug, "File deleted {0}", filePath);
                        }

                        try
                        {
                            RecursivelyRemoveDirectoriesIfEmpty(_fileSystem.Path.GetDirectoryName(filePath));
                        }
                        catch(DirectoryNotFoundException)
                        {
                            //no op
                        }
                    });
            }
            Status = ServiceStatus.Cancelled;
            _logger.Log(LogLevel.Information, "Cancellation requested.");
        }

        private void RecursivelyRemoveDirectoriesIfEmpty(string dirPath)
        {
            if (_payloadDirectory.Equals(dirPath, StringComparison.OrdinalIgnoreCase) ||
                !_fileSystem.Directory.Exists(dirPath))
            {
                return;
            }

            var filesInDir = _fileSystem.Directory.GetFiles(dirPath);
            var dirsInDir = _fileSystem.Directory.GetDirectories(dirPath);
            if (filesInDir.Length + dirsInDir.Length == 0)
            {
                try
                {
                    _logger.Log(LogLevel.Debug, "Deleting directory {0}", dirPath);
                    _fileSystem.Directory.Delete(dirPath);
                    RecursivelyRemoveDirectoriesIfEmpty(_fileSystem.Directory.GetParent(dirPath).FullName);
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, ex, $"Error deleting directory {dirPath}.");
                }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var task = Task.Run(() =>
            {
                BackgroundProcessing(cancellationToken);
            });

            Status = ServiceStatus.Running;
            if (task.IsCompleted)
                return task;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Disk Space Reclaimer Hosted Service is stopping.");
            Status = ServiceStatus.Stopped;
            return Task.CompletedTask;
        }
    }
}