namespace Expecto

open System
open FsCheck
open Expecto

[<AutoOpen; CompilationRepresentationAttribute(CompilationRepresentationFlags.ModuleSuffix)>]
module ExpectoFsCheck =

  let private propertyTest methodName focusState configs name property =

    /// This running plugs into FsCheck and gives us the ability to get called
    /// by the framework on start/finish of fixtures.
    let runner (config: FsCheckConfig) =
      { new IRunner with
          /// Called before a group of properties on a type are checked.
          member __.OnStartFixture _ =
            ()

          /// Called whenever arguments are generated and after the test is run.
          member __.OnArguments (testNumber, args, formatOnEvery) =
            config.receivedArgs config name testNumber args
            |> Async.RunSynchronously

          /// Called on a succesful shrink.
          member __.OnShrink (values, formatValues) =
            config.successfulShrink config name values
            |> Async.RunSynchronously

          /// Called whenever all tests are done, either True, False or Exhausted.
          member __.OnFinished(fsCheckTestName, testResult) =
            let testData =
              match testResult with
              | TestResult.Passed(testData, _) -> testData
              | TestResult.Failed(testData, _, _, _, _, _, _) -> testData
              | TestResult.Exhausted testData -> testData

            let testData = {
              FsCheckTestData.Stamps = testData.Stamps
              NumberOfTests = testData.NumberOfTests
              NumberOfShrinks = testData.NumberOfShrinks
              Labels = testData.Labels
            }

            config.finishedTest config fsCheckTestName testData |> Async.RunSynchronously

            let numTests i = if i = 1 then "1 test" else sprintf "%i tests" i

            let stampsToString s =
              let entry (p,xs) = sprintf "%A%s %s" p "%" (String.concat ", " xs)
              match Seq.map entry s |> Seq.toList with
              | []  -> ""
              | [x] -> sprintf " (%s)\n" x
              | xs  -> sprintf "%s\n" (String.concat "\n" xs)

            match testResult with
            | TestResult.Passed (_testData,_b) -> ()

            | TestResult.Failed (_,_,_, Outcome.Failed (:? IgnoreException as e),_,_,_) ->
              raise e

            | TestResult.Failed (data, original, shrunk, outcome,originalSeed,finalSeed,size) ->
              let parameters =
                original
                |> List.map (sprintf "%A")
                |> String.concat " "
                |> sprintf "Parameters:\n\t%s"

              let shrunk =
                if data.NumberOfShrinks > 0 then
                  shrunk
                  |> List.map (sprintf "%A")
                  |> String.concat " "
                  |> sprintf "\nShrunk %i times to:\n\t%s" data.NumberOfShrinks
                else ""

              let labels =
                match data.Labels.Count with
                | 0 -> String.Empty
                | 1 -> sprintf "Label of failing property: %s\n"
                          (Set.toSeq data.Labels |> Seq.head)
                | _ -> sprintf "Labels of failing property (one or more is failing): %s\n"
                          (String.concat " " data.Labels)

              let original =
                sprintf "Original seed: (%A, %A)" originalSeed.Seed originalSeed.Gamma
              let focus =
                sprintf "Focus on error:\n\t%s (%A, %A, Some %A) \"%s\"" methodName finalSeed.Seed finalSeed.Gamma size name

              sprintf "Failed after %s. %s%s\nResult:\n\t%A\n%s%s%s\n%s"
                      (numTests data.NumberOfTests) parameters shrunk
                      outcome labels (stampsToString data.Stamps) original focus
              |> FailedException
              |> raise

            | TestResult.Exhausted data ->
              sprintf "Exhausted after %s%s"
                (numTests data.NumberOfTests) (stampsToString data.Stamps)
              |> FailedException
              |> raise
      }

    let test (config: FsCheckConfig) =

      let config =
        Config.Default
            .WithMaxTest(config.maxTest)
            .WithReplay(Option.map (fun (seed,gamma,size) -> {Rnd = Rnd(seed, gamma); Size = size}) config.replay)
            .WithName(name)
            .WithStartSize(config.startSize)
            .WithEndSize(config.endSize)
            .WithQuietOnSuccess(true)
            .WithArbitrary(config.arbitrary)
            .WithRunner(runner config)


      Check.One(config, property)
      |> async.Return

    let testCode =
      match configs with
      | None ->
        AsyncFsCheck (None, None, test)
      | Some (testConfig, stressConfig) ->
        AsyncFsCheck (Some testConfig, Some stressConfig, test)

    TestLabel(name, TestCase (testCode, focusState), focusState)

  /// Builds a test property with config
  let testPropertyWithConfigs testConfig stressConfig name =
    propertyTest "etestPropertyWithConfigs" Normal
                 (Some(testConfig, stressConfig)) name

  /// Builds an ignored test property with an explicit config.
  let ptestPropertyWithConfigs testConfig stressConfig name =
    propertyTest "etestPropertyWithConfigs" Pending
                 (Some (testConfig,stressConfig)) name

  /// Builds an ignored test property with an explicit config.
  let ftestPropertyWithConfigs testConfig stressConfig name =
    propertyTest "etestPropertyWithConfigs" Focused
                 (Some (testConfig,stressConfig)) name

  /// Builds a test property with config that will make Expecto to
  /// ignore other unfocused tests and use an error stdGen.
  let etestPropertyWithConfigs stdGen testConfig stressConfig name =
    let testConfig = { testConfig with replay = Some stdGen }
    let stressConfig = { stressConfig with replay = Some stdGen }
    propertyTest "etestPropertyWithConfigs" Focused
                 (Some(testConfig,stressConfig)) name

  let testPropertyWithConfigsStdGen stdGen testConfig stressConfig name =
    let testConfig = { testConfig with replay = Some stdGen }
    let stressConfig = { stressConfig with replay = Some stdGen }
    propertyTest "testPropertyWithConfigsStdGen" Normal (Some (testConfig,stressConfig)) name

  /// Builds a test property with config
  let testPropertyWithConfig config name =
    propertyTest "etestPropertyWithConfig" Normal (Some (config,config)) name

  /// Builds a test property with config that will be ignored by Expecto.
  let ptestPropertyWithConfig config name =
    propertyTest "etestPropertyWithConfig" Pending (Some(config,config)) name

  /// Builds a test property with config that will make Expecto
  /// ignore other unfocused tests
  let ftestPropertyWithConfig config name =
    propertyTest "etestPropertyWithConfig" Focused (Some(config,config)) name

  /// Builds a test property with config that will make Expecto
  /// ignore other unfocused tests and use an error stdGen.
  let etestPropertyWithConfig stdGen config name =
    let config = { config with replay = Some stdGen }
    propertyTest "etestPropertyWithConfig" Focused (Some(config,config)) name

  /// Builds a test property with a config and a random seed number to ensure FsCheck runs identically every time.
  let testPropertyWithConfigStdGen stdGen config name =
    let config = { config with replay = Some stdGen }
    propertyTest "testPropertyWithConfigStdGen" Normal (Some(config,config)) name

  /// Builds a test property.
  let testProperty name =
    propertyTest "etestProperty" Normal None name

  /// Builds a test property that will be ignored by Expecto.
  let ptestProperty name =
    propertyTest "etestProperty" Pending None name

  /// Builds a test property that will make Expecto to ignore other unfocused tests.
  let ftestProperty name =
    propertyTest "etestProperty" Focused None name

  /// Builds a test property that will make Expecto focus on this test use an error stdGen.
  let etestProperty stdGen name =
    let config = { FsCheckConfig.defaultConfig with replay = Some stdGen }
    propertyTest "etestProperty" Focused (Some(config,config)) name


type FsCheck =
  static member Property(name, property: Func<_,bool>) =
    testProperty name property.Invoke

  static member Property(name, property: Func<_,_,bool>) =
    testProperty name (fun a b -> property.Invoke(a,b))

  static member Property(name, property: Func<_,_,_,bool>) =
    testProperty name (fun a b c -> property.Invoke(a,b,c))

  static member Property(name, property: Func<_,_,_,_,bool>) =
    testProperty name (fun a b c d -> property.Invoke(a,b,c,d))