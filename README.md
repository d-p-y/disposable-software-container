# DisposableSoftwareContainer.Docker

## What is this?

Collection of wrappers around [Docker](https://www.docker.com/) command line tools. It auto starts Docker Machine (if needed) then auto builds Docker container using single line of code. Internally it uses IDisposable so that container is cleaned up when it goes out of scope. It uses convention to specify optional build and run parameters. Primary use case justifying its implementation was its usage in automated tests.
At this moment it only supports [Docker Toolkit based Docker](https://www.docker.com/products/docker-toolbox) on Windows. It also assumes that sh.exe (or bash.exe) is in PATH environment variable. [If not one needs to add it](https://www.google.com/search?hl=en&q=Git+For+Windows).

Personally author wrote it to provide instance of the recently released [Microsoft's Sql Server for Linux](https://hub.docker.com/r/microsoft/mssql-server-linux/) to tests [Statically Typed Poco Queries](https://github.com/d-p-y/stpq/).

## How to install it?

Install it from nuget using Visual Studio's dialogs or from command line
```
Install-Package DisposableSoftwareContainer.Docker
```

... or compile it from sources.

## How to use it?

Once added to project one simply needs to build instance of AutostartedDockerContainer class. 
Its sole obligatory parameter is a path to folder containing DockerFile. One needs to create folder with DockerFile in it. In the same folder there can be optional files named ```run.args``` and ```build.args```. Thus on disk it is:

Assumming there is a folder ```c:\Project``` on disk with following files in it:
```
DockerFile    - mandatory file
build.args    - optional file
run.args      - optional file
```

one would need to call ``new AutostartedDockerContainer("c:\project")``` in source code.

NOTE: tests use [Xunit testing library](https://xunit.github.io) but it is in no way needed or related to project.

### Full example on how to use it in C&#35;

``` csharp
using Xunit;
using DisposableSoftwareContainer;

public class SomeTests {
	[Fact]
	public void SomeTest() {
		using (var sqlServerCont = new Docker.AutostartedDockerContainer(@"..\some-path-to-folder-containing-a-DockerFile")) {
			//at this moment container is started and one can access it. 
		}
		//here container is killed, removed and its image is removed as well
	}
}
```

Assuming DockerFile builds ftp server that serves its anonymous users a file named "file.txt"

``` csharp
using Xunit;
using DisposableSoftwareContainer;
using System.Net.Http;
using System.Net;

public class Tests {
	[Fact]
	public void SomeTestRelyingOnFtpServer() {
		using (var service = new Docker.AutostartedDockerContainer(@"..\some-path-to-folder-containing-a-DockerFile")) {
			//at this moment container is started and one can access it
			//container IP can be found using: service.IpAddress
	
			var request = new WebClient();
			request.Credentials = new NetworkCredential("anonymous", "some@example.com");
			var bytes = request.DownloadData($"ftp://{service.IpAddress}/file.txt");
		}
		//here container is killed, removed and its image is removed as well
    }
}
```

### Full example on how to use it in F&#35;

It is exactly the same code as in C&#35;, just more concise.

``` fsharp
open DisposableSoftwareContainer.Docker
open Xunit

	[<Fact>]
	let ``some test needing infrastructure provided by docker machine`` () =
		use service = new AutostartedDockerContainer("..\some-path-to-folder-containing-a-DockerFile")
		
		//at this moment container is started and one can access it
		//container IP can be found using: service.IpAddress
		
		//when 'service' value goes out of scope container is killed, removed and its image is removed as well
```

Assuming DockerFile builds ftp server that serves its anonymous users a file named "file.txt"

``` fsharp
open DisposableSoftwareContainer.Docker
open Xunit
open System.Net.Http
open System.Net

	[<Fact>]
	let ``some test relying on ftp server`` () =
		use service = new AutostartedDockerContainer("..\some-path-to-folder-containing-a-DockerFile")
		
		let request = new WebClient()
		request.Credentials <- NetworkCredential("anonymous", "some@example.com")
		let bytes = sprintf "ftp://%s/file.txt" service.IpAddress |> request.DownloadData
		()
```

## Advanced usage

### Optional runtime and build time parameters

Most of the time one needs to provide some ```docker run``` / ```docker create``` command line arguments. Frequently it will be publish ports parameter 
```-p hostPortNo:containerPortNo``` 

Less often one would like to pass environment variable 
```-e SomeEnvVar=NeededValueOfVar``` during build. In order to meet those goals in the easiest and most decoupled way following convention is assumed:

Author uses it to make us of [Sql Server for Linux image](https://github.com/d-p-y/stpq/tree/master/StaTypPocoQueries.AsyncPoco.Tests/SqlServerInDocker).

### Optional parameters in AutostartedDockerContainer class

//TODO cleanMode, logger, shExePath

### DockerMachine class

//TODO

### Real world example

See tests for [StaTypPocoQueries.Tests project](https://github.com/d-p-y/stpq/tree/master/StaTypPocoQueries.AsyncPoco.Tests).

### Copyright and license info

Copyright © 2016 Dominik Pytlewski. Licensed under Apache License 2.0. See LICENSE file for details
