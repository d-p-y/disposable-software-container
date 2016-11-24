namespace DisposableSoftwareContainer.Docker.Tests

open System
open System.IO
open System.Reflection
open Xunit
open DisposableSoftwareContainer.Docker
open System.Net.Http

type Tests(helper:Xunit.Abstractions.ITestOutputHelper) = 
    let binPath () = 
        //resharper shadow copy workaround thanks to mcdon
        // http://stackoverflow.com/questions/16231084/resharper-runs-unittest-from-different-location

        (Assembly.GetExecutingAssembly().CodeBase |> Uri).LocalPath |> Path.GetDirectoryName
    
    let ipV4AddressPattern = "^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$"

    [<Fact>]
    let ``verify that starting and stopping docker machine works`` () =
        let sut = DockerMachine(Action<_> helper.WriteLine)
        let wasRunning = sut.IsRunning
                
        do if wasRunning then Assert.Matches(ipV4AddressPattern, sut.IpAddress)

        if wasRunning then sut.Stop() else sut.StartIfNeeded()
        
        Assert.NotEqual(sut.IsRunning, wasRunning)

        do if sut.IsRunning then Assert.Matches(ipV4AddressPattern, sut.IpAddress)

        if sut.IsRunning then sut.Stop() else sut.StartIfNeeded()
        
        Assert.Equal(sut.IsRunning, wasRunning)

    [<Fact>]
    let ``verify that autostarted container life cycle works`` () =
        let exampleDockerFileFolder = Path.Combine(binPath (), @"..\..\TestNginxDockerFile")
        helper.WriteLine exampleDockerFileFolder
        
        let helper () = 
            use sut = new AutostartedDockerContainer(exampleDockerFileFolder, logger=Action<_> helper.WriteLine)
            
            let ip = sut.IpAddress
            Assert.Matches(ipV4AddressPattern, ip)

            let cl = new HttpClient()

            let html = 
                async {
                    let! result = cl.GetAsync(sprintf "http://%s:18181/" ip) |> Async.AwaitTask
                    return! result.Content.ReadAsStringAsync() |> Async.AwaitTask
                } |> Async.RunSynchronously

            Assert.Contains("Hello world", html)

            match sut.Container.State with
            |ContainerState.StartedContainer(_,_) -> ()
            |x -> Assert.True(false, sprintf "expected container to be in state StartedContainer but it is %A" x)

            sut

        let sut = helper ()

        match sut.Container.State with
        |ContainerState.NotBuilt -> ()
        |x -> Assert.True(false, sprintf "expected container to be in state NotBuilt but it is %A" x)
