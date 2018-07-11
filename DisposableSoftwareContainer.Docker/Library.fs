//Copyright © 2018 Dominik Pytlewski. Licensed under Apache License 2.0. See LICENSE file for details

module DisposableSoftwareContainer.Docker

open System.Diagnostics
open System.IO
open System.Text.RegularExpressions
open System.Runtime.InteropServices

type ContainerState =
|NotBuilt
|BuiltImage of imageId:string
|BuiltContainer of containerId:string * imageId:string
|StartedContainer of containerId:string * imageId:string

let captureStdout logger startDir program args =
    let result = System.Text.StringBuilder()

    use proc = new Process()
    do
        match startDir with
        |Some startDir -> proc.StartInfo.WorkingDirectory <- startDir
        |None -> ()

    proc.StartInfo.UseShellExecute <- false
    proc.StartInfo.CreateNoWindow <- true
    proc.StartInfo.FileName <- program
    proc.StartInfo.Arguments <- args
    proc.StartInfo.RedirectStandardOutput <- true
    proc.EnableRaisingEvents <- true    
    proc.OutputDataReceived.AddHandler(DataReceivedEventHandler(fun _ x -> result.AppendLine(x.Data) |> ignore))

    do
        match logger with
        |Some logger -> sprintf "running command[%s] with args[%s] in workdir[%s]" program args proc.StartInfo.WorkingDirectory |> logger
        |_ -> ()

    proc.Start() |> ignore
    proc.BeginOutputReadLine()
    
    proc.WaitForExit()
    match proc.ExitCode with
    | 0 -> result.ToString()
    | _ -> failwithf "process %s %s returned error exit code %i" program args proc.ExitCode

let tryGetFullPathForExeFile fileName =
    (System.Environment.GetEnvironmentVariable "PATH").Split(';')
    |> Seq.ofArray
    |> Seq.tryFind (fun dir -> 
        let fullPath = Path.Combine(dir, fileName)
        File.Exists fullPath)
    |> function
    |None -> None
    |Some dir -> Path.Combine(dir, fileName) |> Some

let getFullPathForExeFile fileName =
    tryGetFullPathForExeFile fileName
    |> function
    |None -> failwithf "could not find directory containing %s using paths from PATH" fileName
    |Some path -> path

// for easier interop from C# make logger a regular action
type DockerMachine([<Optional;DefaultParameterValue(null)>]?logger) =
    let dockerMachineExe = getFullPathForExeFile "docker-machine.exe"
    let log = 
        match logger with
        |Some(x:System.Action<string>) -> (fun y -> x.Invoke(y))
        |None -> (fun _ -> ())

    do sprintf "detected docker-machine.exe to be under %s" dockerMachineExe |> log

    let captureStdout = captureStdout (Some log) 

    member __.IpAddress with get() = (captureStdout None dockerMachineExe "ip").TrimEnd()

    member __.Stop () = 
        log "docker-machine stopping"
        captureStdout None dockerMachineExe "stop" |> ignore
        log "docker-machine is stopped"

    member __.Start () = 
        log "docker-machine is starting"
        captureStdout None dockerMachineExe "start" |> ignore
        log "docker-machine is started"

    member __.IsRunning 
        with get() =
            match (captureStdout None dockerMachineExe "status").TrimEnd() with
            | "Running" -> true
            | "Stopped" -> false
            | x -> failwithf "unknown status %s" x
                
    member this.StartIfNeeded () =
        if not <| this.IsRunning then 
            log "docker-machine reports that it is stopped"
            this.Start ()    
        else
            log "docker-machine reports that it is already started"
        
type Args = WithBuildAndRun|WithoutBuild|WithoutRun|Detect
type CleanMode = ContainerAndImage|ContainerOnly

let nullLog = System.Action<string>(fun _ -> ())

//logger has more digestable signature for sake of interop with C#
type DockerContainer(dockerFileAndScriptsFolder, [<Optional;DefaultParameterValue(null)>]?args, 
                     [<Optional;DefaultParameterValue(null)>]?cleanMode,
                     [<Optional;DefaultParameterValue(null)>]?shExePath, 
                     [<Optional;DefaultParameterValue(null)>]?startDockerMachine, 
                     [<Optional;DefaultParameterValue(null)>]?logger) = 

    let mutable state = NotBuilt
    let cleanMode = defaultArg cleanMode ContainerAndImage
    let log = 
        match logger with
        |Some(x:System.Action<string>) -> (fun y -> x.Invoke(y))
        |None -> (fun _ -> ())

    let buildArgsFile = "build.args"
    let runArgsFile = "run.args"
    let shExePath = 
        match shExePath with
        | Some x -> x
        | None ->
            match (tryGetFullPathForExeFile "sh.exe"), (tryGetFullPathForExeFile "bash.exe") with
            | Some x, _ -> x
            | _, Some x -> x
            | _, _ -> failwithf "Could not detect path to neither sh.exe nor bash.exe"

    do sprintf "detected shell to be under %s" shExePath |> log

    let captureStdout = captureStdout (Some log)
    let startDockerMachine = defaultArg startDockerMachine true

    let hasBuildArgs = 
        let args = defaultArg args Detect

        match args with
        |WithBuildAndRun -> true
        |WithoutBuild -> false
        |WithoutRun -> true
        |Detect -> System.IO.Path.Combine(dockerFileAndScriptsFolder, buildArgsFile) |> File.Exists
                
    let hasRunArgs = 
        let args = defaultArg args Detect

        match args with
        |WithBuildAndRun -> true
        |WithoutBuild -> true
        |WithoutRun -> false
        |Detect -> System.IO.Path.Combine(dockerFileAndScriptsFolder, runArgsFile) |> File.Exists
        
    let runDockerCommand cmd =
        sprintf """ -c "eval \"$(docker-machine env --shell=bash default)\"; %s" """ cmd 
        |> captureStdout (Some dockerFileAndScriptsFolder) shExePath 
        
    let getBuildArgs () = 
        if hasBuildArgs then
            System.IO.Path.Combine(dockerFileAndScriptsFolder, buildArgsFile) |> System.IO.File.ReadAllText
        else ""
        
    let getRunArgs () = 
        if hasRunArgs then
            System.IO.Path.Combine(dockerFileAndScriptsFolder, runArgsFile) |> System.IO.File.ReadAllText
        else ""

    let buildImage () = 
        sprintf "starting image build" |> log
        let output = 
            sprintf "docker build %s ." (getBuildArgs ())  
            |> runDockerCommand
        let pattern = "Successfully[\s]+built[\s]+([^\r\n$]+)"
        let reImageId = Regex(pattern, RegexOptions.IgnoreCase)
        
        match reImageId.Match output with
        |x when x.Success -> 
            let imageId = x.Groups.[1].Value
            sprintf "image build succeeded with id=%s" imageId |> log
            imageId
        |_ -> failwithf "couldn't find image id in docker build pattern=%s output=%s" pattern output
        
    let buildContainer imageId =
        sprintf "building container" |> log
        let containerId = 
            let x = 
                sprintf "docker create %s %s" (getRunArgs ()) imageId 
                |> runDockerCommand
            x.TrimEnd()
        sprintf "container built with id=%s" containerId |> log
        containerId

    let startContainer containerId = 
        sprintf "starting container id=%s" containerId |> log
        sprintf "docker start %s" containerId |> runDockerCommand |> ignore
        "container started" |> log
                    
    interface System.IDisposable with
        member __.Dispose () = 
            match state with
            |NotBuilt -> 
                "dispose: noting to cleanup" |> log
                ()
            |BuiltImage(imageId) -> 
                sprintf "dispose: removing image %s" imageId |> log
                sprintf "docker rmi %s" imageId |> runDockerCommand |> ignore
                state <- NotBuilt
            |BuiltContainer(containerId, imageId) -> 
                sprintf "dispose: removing container %s" containerId |> log
                sprintf "docker rm %s" containerId |> runDockerCommand |> ignore
                state <- BuiltImage(imageId)
                
                sprintf "dispose: removing image %s" imageId |> log
                sprintf "docker rmi %s" imageId |> runDockerCommand |> ignore
                state <- NotBuilt
            |StartedContainer(containerId, imageId) -> 
                sprintf "dispose: killing container %s" containerId |> log
                sprintf "docker kill %s" containerId |> runDockerCommand |> ignore
                state <- BuiltContainer(containerId, imageId)

                sprintf "dispose: removing container %s" containerId |> log
                sprintf "docker rm %s" containerId |> runDockerCommand |> ignore
                state <- BuiltImage(imageId)

                match cleanMode with
                |ContainerAndImage ->
                    sprintf "dispose: removing image %s" imageId |> log
                    sprintf "docker rmi %s" imageId |> runDockerCommand |> ignore
                |_ -> sprintf "dispose: not removing image %s" imageId |> log
                state <- NotBuilt

    member __.IpAddress 
        with get() = 
            let mach = DockerMachine(defaultArg logger nullLog)
            if mach.IsRunning then mach.IpAddress else failwithf "docker machine is not running"

    member __.State with get() = state
    member __.Start () =
        do if startDockerMachine then DockerMachine(defaultArg logger nullLog).StartIfNeeded()

        let imageId = buildImage ()
        state <- BuiltImage(imageId)

        let containerId = buildContainer imageId
        state <- BuiltContainer(containerId, imageId)

        startContainer containerId
        state <- StartedContainer(containerId, imageId)
        
type AutostartedDockerContainer(dockerFileAndScriptsFolder:string, 
                                [<Optional;DefaultParameterValue(null)>]?args, 
                                [<Optional;DefaultParameterValue(null)>]?cleanMode,
                                [<Optional;DefaultParameterValue(null)>]?shExePath, 
                                [<Optional;DefaultParameterValue(null)>]?startDockerMachine, 
                                [<Optional;DefaultParameterValue(null)>]?logger) =

    let instance = 
        new DockerContainer(dockerFileAndScriptsFolder, ?args = args, ?cleanMode = cleanMode,?shExePath = shExePath, 
                            ?startDockerMachine = startDockerMachine, ?logger = logger)

    do instance.Start()
        
    interface System.IDisposable with member __.Dispose() = (instance :> System.IDisposable).Dispose()

    member __.Container = instance
    member __.IpAddress = instance.IpAddress
