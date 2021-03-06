# Configuration

## Helm Chart Configuration

Clara DICOM Adapter helm chart is stored in `~/.clara/charts/dicom-adapter/` if installed using Clara CLI. The following configuration files may be modified according to environment requirements:

### ~/.clara/charts/dicom-adapter/values.yaml

```yaml


dicomAdapter:
  dicomPort: 104 # default DICOM SCP listening port.
  apiPort: 5000 # default port for the web API.

storage:
  volumeSize: 50Gi # disk space allocated for DICOM Adapter used for temporarily storing of received DICOM instances.
  hostPath: "/clara-io" # host path mounted into /payloads in the container for storing temporary files.

database:
  volumeSize: 3Gi # disk space allocated for the DICOM Adapter database.  Default uses sqlite3 database.
  hostPath: "/clara-io/dicom-adapter" # host path mounted into /database for storing the sqlite3 database file.

```


### ~/.clara/charts/dicom-adapter/files/appsettings.json

```json
{
  "DicomAdapter": {
    "dicom": {
      "scp": {
        "port": 104, // DICOM SCP listening port. (default 104)
        "maximumNumberOfAssociations": 25, // maximum number of concurrent associations. (range: 1-1000, default: 25)
        "verification": {
          "enabled": true, // respond to c-ECHO commands (default: true)
          "transferSyntaxes": [
            "1.2.840.10008.1.2.1", // Explicit VR Little Endian
            "1.2.840.10008.1.2" , // Implicit VR Little Endian
            "1.2.840.10008.1.2.2", // Explicit VR Big Endian
          ]
        }
        "logDimseDatasets": false, // whether or not to write command and dataset to log (default false)
        "rejectUnknownSources": true // whether to reject unknown sources not listed in the source section. (default true)
      },
      "scu": {
        "export": {
          "maximumRetries": 3, // number of retries the exporter shall perform before reporting failure to Results Service.
          "failureThreshold" 0.5, // failure threshold for a task to be marked as failure.
          "pollFrequencyMs": 500 // number of milliseconds each exporter shall poll tasks from Results Service,
        },
        "aeTitle": "ClaraSCU", // AE Title of the SCU service
        "logDimseDatasets": false,  // whether or not to write command and data datasets to the log.
        "logDataPDUs": false, // whether or not to write message to log for each P-Data-TF PDU sent or received
        "maximumNumberOfAssociations": 8, // maximum number of outbound DICOM associations (range: 1-100, default: 8)
      }
    },
    "services": {
      "platform": {
        "maxRetries": 3, // maximum number of retries to be performed when an execution attempt fails to connect to connect o Clara Platform.
        "retryDelaySeconds": 180, // number of seconds to wait before attempting to retry.
        "uploadMetadata": false, // whether or not to upload metadata with the associated job defined in the `metadataDicomSource` property.
        "metadataDicomSource": [ // list of DICOM tags that are used when extracting metadata to be associated with an inference job.
          "0008,0020",
          "0008,0060",
          "0008,1030",
          "0008,103E",
          "0010,0020",
          "0010,0030",
          "0010,1010",
          "0020,000D"
        ]
      }
    },
    "storage" : {
      "temporary" : "/payloads", // storage path used for storing received instances before uploading to Clara Platform.
      "watermarkPercent": 85, // storage space usage watermark to stop storing, exporting and retrieving of DICOM instances.
      "reserveSpaceGB": 5 // minimal storage space required to store, export and retrieve DICOM instances.
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Dicom": "Information",
      "System": "Warning",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Warning",
      "Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker": "Error",      
      "Nvidia": "Information",
      "Nvidia.Clara.DicomAdapter.Server.Services.Disk": "Information",
      "Nvidia.Clara.DicomAdapter.Server.Services.Export": "Information",
      "Nvidia.Clara.DicomAdapter.Server.Services.Http": "Information",
      "Nvidia.Clara.DicomAdapter.Server.Services.Jobs": "Information",
      "Nvidia.Clara.DicomAdapter.Server.Services.Scp": "Information"
    },
    "Console": {
      "disableColors": true
    }
  },
  "AllowedHosts": "*"
}
```

## Configuration Validation

Clara DICOM Adapter validates all settings during startup. Any provided values that are invalid
or missing may cause the service to crash. If you are the running the DICOM Adapter inside
Kubernetes/Helm, you may see the `CrashLoopBack` error.  To review the validation errors, simply
run `kubectl logs <name-of-dicom-adapter-pod>`.

## Logging

DICOM Adapter, by default, write all logs to console.  If DICOM Adapter is running inside a Docker container, additional configuration may be required to limit the size to prevent filling up storage space.  Please refer to [Docker](https://docs.docker.com/config/containers/logging/configure/) for additional information.


### Log Levels
Log level may be adjusted per module basis.  For example, given the following log entries:

```
10:31:03 info: Nvidia.Clara.DicomAdapter.Server.Processors.AeTitleJobProcessor[0]
      Initializing AE Title DicomWebTest with processor Nvidia.Clara.DicomAdapter.Server.Processors.AeTitleJobProcessor, Nvidia.Clara.DicomAdapter
10:31:03 info: Nvidia.Clara.DicomAdapter.Server.Processors.AeTitleJobProcessor[0]
      AE Title DicomWebTest Processor Setting: timeout=5s
```

By default, the `Nvidia` namespace is set to log all `Information` level logs.  If additional information is required to debug the **AE Title Job Processor module** or to turn down the noise, simply add a new entry under the `LogLevel` section of the configuration file to adjust it:

```
 "Logging": {
    "LogLevel": {
      "Nvidia": "Information",
      "Nvidia.Clara.DicomAdapter.Server.Processors.AeTitleJobProcessor": "Debug",
      ...
```

The following log level may be used:

* Trace
* Debug
* Information
* Warning
* Error
* Critical
* None

Additional information may be found on `docs.microsoft.com`:
* [LogLevel Enum](https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.loglevel)
* [Logging in .NET](https://docs.microsoft.com/en-us/dotnet/core/extensions/logging)


## Job Metadata

By default, DICOM Adapter uploads the following fields to the Clara Platform Metadata Store:

1. **Source**: Source of the DICOM instances. It could either be the AE Title if the DICOM instances were received via SCP or the *Transaction ID* if requested through the inference API.
2. **Instances**: Number of DICOM instances associated with this job.

Additional metadata can be uploaded by turning on `uploadMetadata` and by specifying the DICOM tags in the `metadataDicomSource` property in the config file.
DICOM Adapter will scan through all received DICOM instances and extract all unique values from the DICOM tags specified.


If a single unique value is found for a DICOM tag, e.g. Patient ID `0010,0020`, a single entry is added to the metadata store:

```yaml
 Metadata:
    00100020:     PID-123456
```

If more than one values are found in, e.g. Study Instance UID `0020,000D`, multiple entries are added to the metadata store:

```yaml
 Metadata:
    0020000D-0:  1.2.276.0.2505010.3.2.3.16.208271309.474136.1617917877.309864
    0020000D-1:  1.2.276.0.2350010.3.3.3.7.2088241309.474136.1617917887.309816
    0020000D-2:  1.2.276.0.2530010.3.4.3.4.2088171309.474136.1617917867.309794
```

> [!WARNING]
> Any PHI extracted from the DICOM instances and uploaded to the Clara Platform Metadata Store are stored in the
> Clara Platform database.  Please refer to the [Clara SDK documentation](https://docs.nvidia.com/clara) for additional information.
