pipeline {
    agent any

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO                = '1'
        PROJECT_DIR                  = 'WillscotAutomation'
        ALLURE_RESULTS               = 'WillscotAutomation/allure-results'
        // Use Prod config: 120s navigation timeout, headless, targets willscot.com
        TEST_ENV                     = 'Prod'
    }

    options {
        timestamps()
        timeout(time: 60, unit: 'MINUTES')
        buildDiscarder(logRotator(numToKeepStr: '20'))
    }

    triggers {
        // Polls GitHub every 2 minutes — works on private networks where
        // GitHub cannot reach Jenkins via webhook (private IP 10.x.x.x).
        pollSCM('H/2 * * * *')
    }

    stages {

        stage('Checkout') {
            steps {
                checkout scm
                echo "Branch: ${env.GIT_BRANCH} | Commit: ${env.GIT_COMMIT?.take(7)}"
            }
        }

        stage('Restore') {
            steps {
                dir("${PROJECT_DIR}") {
                    bat 'dotnet restore --locked-mode 2>nul || dotnet restore'
                }
            }
        }

        stage('Build') {
            steps {
                dir("${PROJECT_DIR}") {
                    bat 'dotnet build --no-restore --configuration Release'
                }
            }
        }

        stage('Install Playwright Browsers') {
            steps {
                dir("${PROJECT_DIR}") {
                    // Installs only Chromium to keep build times short.
                    // Change to 'install --with-deps' if Firefox/WebKit tests are added.
                    bat 'powershell -NonInteractive -ExecutionPolicy Bypass -File "bin\\Release\\net8.0\\playwright.ps1" install chromium'
                }
            }
        }

        stage('Run Tests') {
            steps {
                dir("${PROJECT_DIR}") {
                    bat '''dotnet test ^
                        --no-build ^
                        --configuration Release ^
                        --settings WillscotAutomation.runsettings ^
                        --logger "trx;LogFileName=jenkins-results.trx" ^
                        --logger "nunit;LogFileName=nunit-results.xml" ^
                        --results-directory TestResults ^
                        -- NUnit.NumberOfTestWorkers=1'''
                }
            }
            post {
                always {
                    // Publish NUnit XML results — NUnitXml.TestLogger emits the format this plugin expects
                    nunit testResultsPattern: 'WillscotAutomation/TestResults/nunit-results.xml',
                          failIfNoResults: false
                }
            }
        }

        stage('Collect Allure Results') {
            steps {
                // dotnet test may run the test host from the bin output directory,
                // so allure-results can land there instead of the project root.
                // Copy both locations into the single path the Allure plugin expects.
                bat """
                    if exist "WillscotAutomation\\bin\\Release\\net8.0\\allure-results" (
                        xcopy /E /I /Y "WillscotAutomation\\bin\\Release\\net8.0\\allure-results" "WillscotAutomation\\allure-results\\"
                    )
                """
            }
        }

        stage('Publish Allure Report') {
            steps {
                // catchError keeps the build GREEN if the report step fails —
                // only test failures should mark a build as failed.
                catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE') {
                    allure([
                        reportBuildPolicy: 'ALWAYS',
                        results          : [[path: "${ALLURE_RESULTS}"]],
                        commandline      : 'allure'
                    ])
                }
            }
        }
    }

    post {
        always {
            script {
                currentBuild.description = """
                    <b>WillScot Homepage Automation</b> &nbsp;|&nbsp; Env: <b>Prod</b> (www.willscot.com) &nbsp;|&nbsp; Browser: Chromium (headless)<br/>
                    <b>17 Scenarios</b>: 14 Active &nbsp;&bull;&nbsp; 3 Skipped (@ignore)<br/>
                    <hr style='margin:3px 0'/>
                    <b>&#128293; Smoke</b>: TC-001 Page Load &bull; TC-003 Learn More CTA &bull; TC-005 Nav Items &bull; TC-008 Page Title &bull; TC-010 Request a Quote Button<br/>
                    <b>&#128204; Navigation</b>: TC-005 Nav Visible &bull; TC-006 Locations &bull; TC-007 Office Trailers for Sale &bull; TC-009 About Us<br/>
                    <b>&#128230; Products</b>: TC-013 Storage Container Card &bull; TC-014 Storage Container Nav &bull; TC-015 Product Images HTTP 200 &bull; TC-016 Product Links HTTP 200<br/>
                    <b>&#128269; Quality</b>: TC-004 No Broken Images / Console Errors / JS Exceptions &bull; TC-008 SEO Title<br/>
                    <b>&#127981; Industry</b>: TC-017 Solution Tabs (6 verticals)<br/>
                    <b>&#9196; Skipped</b>: TC-002 Hero Headline &bull; TC-011 Quote Nav &bull; TC-012 Support Nav<br/>
                    <hr style='margin:3px 0'/>
                    <b>Framework:</b> Playwright 1.44 + Reqnroll 2.2 + NUnit 4 &nbsp;|&nbsp; <b>Workers:</b> 1 (sequential)
                """.stripIndent().trim()
            }
            cleanWs(
                cleanWhenSuccess: false,   // keep workspace on success for re-runs
                cleanWhenFailure: false,   // keep workspace on failure to inspect artifacts
                cleanWhenAborted: true
            )
        }
    }
}
