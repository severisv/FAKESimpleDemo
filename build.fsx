// include Fake lib
#r @"packages/FAKE/tools/FakeLib.dll"
open Fake
open Fake.Testing
open Fake.AppVeyor
open Fake.NuGetHelper
open Fake.OctoTools
open System.IO


[<AutoOpen>]
module OctoHelpers =
  let executeOcto command =
    let serverName = environVar "OCTO_SERVER"
    let apiKey = environVar "OCTO_KEY"
    let server = { Server = serverName; ApiKey = apiKey }
    Octo (fun octoParams ->
        { octoParams with
            ToolPath = "./packages/octopustools"
            Server   = server
            Command  = command }
    )

[<AutoOpen>]
module AppVeyorHelpers =
  let execOnAppveyor arguments =
    let result =
      ExecProcess (fun info ->
        info.FileName <- "appveyor"
        info.Arguments <- arguments
        ) (System.TimeSpan.FromMinutes 2.0)
    if result <> 0 then failwith (sprintf "Failed to execute appveyor command: %s" arguments)
    trace "Published packages"

  let publishOnAppveyor folder =
    !! (folder + "*.nupkg")
    |> Seq.iter (fun artifact -> execOnAppveyor (sprintf "PushArtifact %s" artifact))


[<AutoOpen>]
module Settings =
  let buildDir = "./.build/"
  let packagingDir = buildDir + "TestApp.Web/_PublishedWebsites/TestApp.Web"
  let deployDir = "./.deploy/"
  let testDir = "./.test/"
  let projects = !! "src/**/*.xproj"
  let testProjects = !! "src/**/*.Tests.csproj"
  let packages = !! "./**/packages.config"

  let getOutputDir proj =
    let folderName = Directory.GetParent(proj).Name
    sprintf "%s%s/" buildDir folderName

  let build proj =
    let outputDir = proj |> getOutputDir
    MSBuildRelease outputDir "ResolveReferences;Build" [proj] |> ignore

  let getVersion() =
    let buildCandidate = (environVar "APPVEYOR_BUILD_NUMBER")
    if buildCandidate = "" || buildCandidate = null then "1.0.0" else (sprintf "1.0.0.%s" buildCandidate)

[<AutoOpen>]
module Targets =
  Target "Clean" (fun() ->
    CleanDirs [buildDir; deployDir; testDir]
  )

  Target "RestorePackages" (fun _ ->
    packages
    |> Seq.iter (RestorePackage (fun p -> {p with OutputPath = "./src/packages"}))
  )

  Target "Build" (fun() ->
    projects
    |> Seq.iter build
  )

   
  Target "Package" (fun _ ->
    trace "Packing the web"
    let version = getVersion()
    NuGet (fun p ->
          {p with
              Authors = ["OPF"]
              Project = "FAKESimple.Web"
              Description = "Demoing FAKE"
              OutputPath = deployDir
              Summary = "Does this work"
              WorkingDir = packagingDir
              Version = version
              Publish = false })
              (packagingDir + "/FAKESimple.Web.nuspec")
  )

  Target "Publish" (fun _ ->
    match buildServer with
    | BuildServer.AppVeyor ->
        publishOnAppveyor deployDir
    | _ -> ()
  )

  Target "Create release" (fun _ ->
    let version = getVersion()
    let release = CreateRelease({ releaseOptions with Project = "FAKESimple.Web"; Version = version }, None)
    executeOcto release
  )

  Target "Deploy" (fun _ ->
    let version = getVersion()
    let deploy = DeployRelease(
                  { deployOptions with
                      Project = "FAKESimple.Web"
                      Version = version
                      DeployTo = "Prod"
                      WaitForDeployment = true})
    executeOcto deploy
  )

  Target "Default" (fun _ ->
    ()
  )

"Clean"
==> "RestorePackages"
==> "Build"
==> "Package"
==> "Publish"
==> "Create release"
==> "Deploy"
==> "Default"

RunTargetOrDefault "Default"
