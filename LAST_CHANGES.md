#F The website and README download links now open the releases page: /releases/latest ignores beta pre-releases and returned a 404 @rickypid
#F The project now uses a main-only branching flow: feature branches start from main and releases are vX.Y.Z tags (0.x betas are published as GitHub pre-releases) @rickypid
##F CI runs on pushes and pull requests to main only, and insideapp.settings.json sets git.baseBranch to main @rickypid
