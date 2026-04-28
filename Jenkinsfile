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
        githubPush()
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
        success {
            echo "All tests passed. Allure report available in the build sidebar."
        }
        failure {
            echo "Build/tests failed. Check Console Output and the Allure report for details."
        }
        always {
            cleanWs(
                cleanWhenSuccess: false,   // keep workspace on success for re-runs
                cleanWhenFailure: false,   // keep workspace on failure to inspect artifacts
                cleanWhenAborted: true
            )
        }
    }
}
