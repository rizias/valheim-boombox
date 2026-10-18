param(
    [ValidateSet('Patch','Minor','Major')][string]$Increment = 'Patch',
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
function Invoke-ReleaseTool([string]$Tool, [string[]]$Arguments) {
    $application = @(Get-Command -Name $Tool -CommandType Application -ErrorAction Stop)[0]
    $priorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& $application.Source @Arguments 2>&1)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $priorPreference }
    [pscustomobject]@{Code=$code;Text=($output | ForEach-Object { "$_" }) -join "`n"}
}
$remote = Invoke-ReleaseTool 'git' @('-C',$PSScriptRoot,'remote','get-url','origin')
if ($remote.Code -ne 0 -or $remote.Text -notmatch '^(?:https://github\.com/|git@github\.com:)([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?/?$') {
    throw 'Set this mod repository origin to its GitHub repository before dispatching a release.'
}
$repository = $Matches[1]
$account = Invoke-ReleaseTool 'gh' @('auth','status','--hostname','github.com')
if ($account.Code -ne 0) { throw 'GitHub is unreachable or GitHub CLI login is unavailable. Check network access and gh auth status; no release was started.' }
$details = Invoke-ReleaseTool 'gh' @('repo','view',$repository,'--json','nameWithOwner,defaultBranchRef')
if ($details.Code -ne 0) { throw 'GitHub repository could not be read. No release was started.' }
$project = $details.Text | ConvertFrom-Json
$branch = $project.defaultBranchRef.name
if (-not $branch) { throw 'Push the source to the GitHub repository before starting a release.' }
$workflow = Invoke-ReleaseTool 'gh' @('api',('repos/' + $repository + '/actions/workflows/release.yml'))
if ($workflow.Code -ne 0) { throw 'Commit and push .github/workflows/release.yml with the mod source first. GitHub does not have this release workflow yet; no job was started.' }
$workflowInfo = $workflow.Text | ConvertFrom-Json
if ($workflowInfo.state -ne 'active') { throw 'Enable the Release workflow in GitHub Actions before dispatching it.' }
$url = 'https://github.com/' + $repository + '/actions/workflows/release.yml'
if ($CheckOnly) {
    Write-Output ('GitHub Actions release is configured for repository ' + $repository + ' on ' + $branch + '. No job was started.')
    exit 0
}
$dispatch = Invoke-ReleaseTool 'gh' @('workflow','run','release.yml','--repo',$repository,'--ref',$branch,'-f',('bump=' + $Increment.ToLowerInvariant()))
if ($dispatch.Code -ne 0) { throw 'GitHub did not accept the release workflow request. No successful release is claimed.' }
Write-Output ('GitHub accepted the ' + $Increment.ToLowerInvariant() + ' release workflow. Build, checks and publication run on GitHub.')
Write-Output $url
