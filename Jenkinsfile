// Deployment pipeline, run by the self-hosted Jenkins in yggdrasil (github.com/artur-rios/yggdrasil).
//
// All the logic lives in that repository's shared library so the four applications deploy the same
// way. What it does with this repository:
//
//   release/x.y.z pushed                 -> deploy to homologation
//   pull request release/x.y.z -> main   -> wait for every GitHub check to pass, deploy to
//                                           production, merge the pull request, tag vx.y.z,
//                                           delete the release branch
//
// Build and test stay in GitHub Actions; this file only deploys.
//
// The library version is deliberately not pinned here: yggdrasil's controller configuration
// (platform/jenkins/controller/casc.yaml) sets `allowVersionOverride: false`, so
// `@Library('yggdrasil@<tag>')` would fail the build. Pin it centrally instead, by setting that
// configuration's `defaultVersion` to a release tag (v0.3.0 at the time of writing).

@Library('yggdrasil') _

yggdrasilPipeline(stack: 'fortuna-api')
