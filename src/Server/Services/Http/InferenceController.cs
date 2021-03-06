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

using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nvidia.Clara.DicomAdapter.API;
using Nvidia.Clara.DicomAdapter.API.Rest;
using Nvidia.Clara.DicomAdapter.Common;
using Nvidia.Clara.DicomAdapter.Configuration;
using Nvidia.Clara.DicomAdapter.Server.Repositories;
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Net;
using System.Threading.Tasks;

namespace Nvidia.Clara.DicomAdapter.Server.Services.Http
{
    [ApiController]
    [Route("api/[controller]")]
    public class InferenceController : ControllerBase
    {
        private readonly IInferenceRequestRepository _inferenceRequestStore;
        private readonly IOptions<DicomAdapterConfiguration> _configuration;
        private readonly ILogger<InferenceController> _logger;
        private readonly IJobs _jobsApi;
        private readonly IFileSystem _fileSystem;

        public InferenceController(
            IInferenceRequestRepository inferenceRequestStore,
            IOptions<DicomAdapterConfiguration> configuration,
            ILogger<InferenceController> logger,
            IJobs jobsApi,
            IFileSystem fileSystem)
        {
            _inferenceRequestStore = inferenceRequestStore ?? throw new ArgumentNullException(nameof(inferenceRequestStore));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobsApi = jobsApi ?? throw new ArgumentNullException(nameof(jobsApi));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        [HttpGet("status/{id}")]
        public async Task<ActionResult> JobStatus(string id)
        {
            Guard.Against.NullOrWhiteSpace(id, nameof(id));

            try
            {
                var status = await _inferenceRequestStore.GetStatus(id);

                if (status is null)
                {
                    return Problem(title: "Inference request not found.", statusCode: (int)HttpStatusCode.NotFound, detail: "Unable to locate the specified request.");
                }

                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, $"Failed to retrieve status for TransactionId/JobId={id}");
                return Problem(title: "Failed to retrieve inference request status.", statusCode: (int)HttpStatusCode.InternalServerError, detail: ex.Message);
            }
        }

        [HttpPost]
        public async Task<ActionResult> NewInferenceRequest([FromBody] InferenceRequest request)
        {
            Guard.Against.Null(request, nameof(request));

            if (!request.IsValid(out string details))
            {
                return Problem(title: $"Invalid request", statusCode: (int)HttpStatusCode.UnprocessableEntity, detail: details);
            }

            using var _ = _logger.BeginScope(new LogginDataDictionary<string, object> { { "TransactionId", request.TransactionId } });

            try
            {
                await CreateJob(request);
                _logger.Log(LogLevel.Information, $"Job created with Clara: JobId={request.JobId}, PayloadId={request.PayloadId}");
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, $"Failed to create job with Clara Platform: TransactionId={request.TransactionId}");
                return Problem(title: "Failed to create job", statusCode: (int)HttpStatusCode.InternalServerError, detail: ex.Message);
            }

            try
            {
                if (_fileSystem.Directory.TryGenerateDirectory(
                    _fileSystem.Path.Combine(_configuration.Value.Storage.TemporaryDataDirFullPath, "irs", request.TransactionId, request.JobId),
                    out string storagePath))
                {
                    request.ConfigureTemporaryStorageLocation(storagePath);
                }
                else
                {
                    throw new InferenceRequestException("Failed to generate a temporary storage location for request.");
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, $"Failed to configure storage location for request: TransactionId={request.TransactionId}");
                return Problem(title: ex.Message, statusCode: (int)HttpStatusCode.InternalServerError, detail: ex.Message);
            }

            try
            {
                await _inferenceRequestStore.Add(request);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, $"Unable to queue the request: TransactionId={request.TransactionId}");
                return Problem(title: "Failed to save request", statusCode: (int)HttpStatusCode.InternalServerError, detail: ex.Message);
            }

            return Ok(new InferenceRequestResponse
            {
                JobId = request.JobId,
                PayloadId = request.PayloadId,
                TransactionId = request.TransactionId
            });
        }

        private async Task CreateJob(InferenceRequest request)
        {
            
            var metadata = new JobMetadataBuilder();
            metadata.AddSourceName($"{request.TransactionId}");

            var job = await _jobsApi.Create(request.Algorithm.PipelineId, request.JobName, request.ClaraJobPriority, metadata);
            request.JobId = job.JobId;
            request.PayloadId = job.PayloadId;
        }
    }
}