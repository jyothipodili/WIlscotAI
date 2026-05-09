pipeline {
    agent any

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO               = '1'
        PROJECT_DIR                 = 'WillscotAutomation'
        TEST_ENV                    = 'Prod'
        IMAGE_NAME                  = 'willscot-automation'
        DOCKER_HOST                 = 'tcp://127.0.0.1:2375'
        K8S_NAMESPACE               = 'willscot'
        MINIKUBE                    = 'C:\\minikube\\minikube.exe'
        KUBECTL                     = 'C:\\minikube\\kubectl.exe'
        MINIKUBE_HOME               = 'C:\\Users\\santhi.podili'
        KUBECONFIG                  = 'C:\\Users\\santhi.podili\\.kube\\config'
        // Artifact paths relative to workspace root
        ALLURE_RESULTS              = 'allure-results'
        ALLURE_REPORT               = 'allure-report'
        TEST_RESULTS_DIR            = 'TestResults'
    }

    options {
        timestamps()
        timeout(time: 120, unit: 'MINUTES')
        buildDiscarder(logRotator(numToKeepStr: '20'))
    }

    triggers {
        pollSCM('H/2 * * * *')
    }

    stages {

        // ── 1. Checkout ──────────────────────────────────────────────────────────
        stage('Checkout') {
            steps {
                checkout scm
                echo "Branch: ${env.GIT_BRANCH} | Commit: ${env.GIT_COMMIT?.take(7)}"
            }
        }

        // ── 2. Build & Package ───────────────────────────────────────────────────
        // Build the .NET project and bake it into a Docker image.
        // Tests do NOT run here — they run inside the K8s pod.
        stage('Build & Package') {
            steps {
                dir("${PROJECT_DIR}") {
                    bat 'dotnet restore --locked-mode 2>nul || dotnet restore'
                    bat 'dotnet build --no-restore --configuration Release'
                }
                bat "docker build -t %IMAGE_NAME%:latest ."
            }
        }

        // ── 3. Load image into Minikube ──────────────────────────────────────────
        stage('Minikube Image Load') {
            steps {
                bat '"%MINIKUBE%" image load willscot-automation:latest'
            }
        }

        // ── 4. Deploy K8s Job ────────────────────────────────────────────────────
        stage('K8s Deploy') {
            steps {
                bat '"%MINIKUBE%" update-context'
                bat '"%KUBECTL%" apply -f k8s/namespace.yaml --validate=false'
                // Delete any previous job so the new one starts clean
                bat '"%KUBECTL%" delete job willscot-automation -n %K8S_NAMESPACE% --ignore-not-found'
                bat '"%KUBECTL%" apply -f k8s/deployment.yaml -n %K8S_NAMESPACE% --validate=false'
                echo 'K8s job submitted — waiting for pod to start...'
                bat '"%KUBECTL%" wait --for=condition=ready pod -l job-name=willscot-automation -n %K8S_NAMESPACE% --timeout=120s || echo Pod not yet ready — will poll in next stage'
            }
        }

        // ── 5. Run Tests (K8s) ───────────────────────────────────────────────────
        // Stream live logs while waiting; mark UNSTABLE (not FAILURE) if tests fail
        // so downstream artifact stages still run.
        stage('Run Tests (K8s)') {
            steps {
                script {
                    // Stream logs in background while polling for completion
                    bat(
                        script: 'for /f "tokens=*" %%p in (\'"%KUBECTL%" get pods -n %K8S_NAMESPACE% -l job-name=willscot-automation -o name\') do "%KUBECTL%" logs -f %%p -n %K8S_NAMESPACE%',
                        returnStatus: true
                    )

                    def rc = bat(
                        script: '"%KUBECTL%" wait --for=condition=complete job/willscot-automation -n %K8S_NAMESPACE% --timeout=1800s',
                        returnStatus: true
                    )
                    if (rc != 0) {
                        bat(
                            script: '"%KUBECTL%" wait --for=condition=failed job/willscot-automation -n %K8S_NAMESPACE% --timeout=60s',
                            returnStatus: true
                        )
                        unstable(message: 'One or more tests failed — review Allure report and Word report below.')
                    }
                }
            }
        }

        // ── 6. Collect Artifacts from K8s Pod ───────────────────────────────────
        // kubectl cp pulls allure-results (contains screenshots, videos, traces as
        // Allure attachments) and the TRX file from the completed pod before TTL
        // cleans it up.
        stage('Collect Artifacts') {
            steps {
                // Clean previous run
                bat '''
                    if exist "allure-results" rd /s /q "allure-results"
                    if exist "TestResults"    rd /s /q "TestResults"
                    mkdir "allure-results"
                    mkdir "TestResults"
                '''
                // Capture pod name and copy artifacts out
                bat '''
                    setlocal enabledelayedexpansion
                    "%KUBECTL%" get pods -n %K8S_NAMESPACE% -l job-name=willscot-automation ^
                        --no-headers -o custom-columns=NAME:.metadata.name 1>pod.txt 2>nul
                    for /f "usebackq tokens=1" %%i in (pod.txt) do set POD=%%i
                    del pod.txt
                    if "!POD!"=="" (
                        echo ERROR: No pod found for willscot-automation job
                        exit /b 1
                    )
                    echo Copying from pod: !POD!
                    "%KUBECTL%" cp %K8S_NAMESPACE%/!POD!:/app/allure-results  allure-results  || echo WARNING: allure-results copy incomplete
                    "%KUBECTL%" cp %K8S_NAMESPACE%/!POD!:/app/TestResults     TestResults     || echo WARNING: TestResults copy incomplete
                    endlocal
                '''
            }
        }

        // ── 7. Publish Allure Report ─────────────────────────────────────────────
        stage('Allure Report') {
            steps {
                catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE') {
                    allure([
                        reportBuildPolicy: 'ALWAYS',
                        results          : [[path: "${ALLURE_RESULTS}"]],
                        commandline      : 'allure'
                    ])
                }
            }
        }

        // ── 8. Word Executive Report ─────────────────────────────────────────────
        // Generates a .docx with test summary, pass/fail table, screenshot links,
        // and pipeline explanation — ready to send to a manager.
        stage('Word Report') {
            steps {
                catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE') {
                    bat '''
                        pip install python-docx Pillow --quiet --disable-pip-version-check 2>nul || echo pip install failed — Python may not be on PATH
                        python scripts\\generate_word_report.py ^
                            --allure-results allure-results ^
                            --trx TestResults\\results.trx ^
                            --output "WillScot_ExecutiveReport_Build%BUILD_NUMBER%.docx" ^
                            --build %BUILD_NUMBER% ^
                            --env %TEST_ENV%
                    '''
                }
            }
        }

        // ── 9. Archive All Evidence ──────────────────────────────────────────────
        stage('Archive Evidence') {
            steps {
                // Allure HTML report (Jenkins Allure plugin also publishes it, but
                // archiving gives a downloadable zip for email/sharing)
                archiveArtifacts artifacts: 'allure-report/**',                   allowEmptyArchive: true
                // Raw Allure results — includes embedded screenshots, videos, traces
                archiveArtifacts artifacts: 'allure-results/**',                  allowEmptyArchive: true
                // TRX + any extra TestResults files
                archiveArtifacts artifacts: 'TestResults/**',                     allowEmptyArchive: true
                // Word executive report
                archiveArtifacts artifacts: 'WillScot_ExecutiveReport_*.docx',   allowEmptyArchive: true
            }
        }

    } // end stages

    // ── Post ─────────────────────────────────────────────────────────────────────
    post {
        always {
            script {
                def status = currentBuild.result ?: 'SUCCESS'
                def icon   = status == 'SUCCESS' ? '&#9989;' : status == 'UNSTABLE' ? '&#9888;' : '&#10060;'
                currentBuild.description = """
                    ${icon} <b>WillScot Homepage Automation</b> &nbsp;|&nbsp; Env: <b>${TEST_ENV}</b> &nbsp;|&nbsp; Build: #${env.BUILD_NUMBER}<br/>
                    <b>17 Scenarios</b>: 14 Active &nbsp;&bull;&nbsp; 3 Skipped (@ignore) &nbsp;|&nbsp; Workers: 4 parallel<br/>
                    <hr style='margin:3px 0'/>
                    <b>&#128293; Smoke</b>: TC-001 &bull; TC-003 &bull; TC-005 &bull; TC-008 &bull; TC-010<br/>
                    <b>&#128204; Navigation</b>: TC-005 &bull; TC-006 &bull; TC-007 &bull; TC-009<br/>
                    <b>&#128230; Products</b>: TC-013 &bull; TC-014 &bull; TC-015 &bull; TC-016<br/>
                    <b>&#128269; Quality</b>: TC-004 &bull; TC-008 &nbsp;|&nbsp; <b>&#127981; Industry</b>: TC-017<br/>
                    <hr style='margin:3px 0'/>
                    <b>Artifacts:</b> Allure Report &bull; Videos (WebM) &bull; Screenshots &bull; Playwright Traces &bull; Word Report
                """.stripIndent().trim()
            }
        }
        cleanup {
            cleanWs(
                cleanWhenSuccess: false,
                cleanWhenFailure: false,
                cleanWhenAborted: true
            )
        }
    }
}

// ── OPTIONAL: Parallel K8s jobs by test category ─────────────────────────────
// Uncomment the block below and replace the 'K8s Deploy' + 'Run Tests' stages
// above to run smoke / navigation / products / quality / industry concurrently.
// Each job needs its own deployment YAML (copy k8s/deployment.yaml, set FILTER).
//
// stage('Run Tests in Parallel') {
//     parallel {
//         stage('Smoke') {
//             steps {
//                 bat '"%KUBECTL%" delete job willscot-smoke -n %K8S_NAMESPACE% --ignore-not-found'
//                 bat '"%KUBECTL%" apply -f k8s/job-smoke.yaml -n %K8S_NAMESPACE% --validate=false'
//                 bat '"%KUBECTL%" wait --for=condition=complete job/willscot-smoke -n %K8S_NAMESPACE% --timeout=600s'
//             }
//         }
//         stage('Navigation') {
//             steps {
//                 bat '"%KUBECTL%" delete job willscot-navigation -n %K8S_NAMESPACE% --ignore-not-found'
//                 bat '"%KUBECTL%" apply -f k8s/job-navigation.yaml -n %K8S_NAMESPACE% --validate=false'
//                 bat '"%KUBECTL%" wait --for=condition=complete job/willscot-navigation -n %K8S_NAMESPACE% --timeout=600s'
//             }
//         }
//         stage('Products') {
//             steps {
//                 bat '"%KUBECTL%" delete job willscot-products -n %K8S_NAMESPACE% --ignore-not-found'
//                 bat '"%KUBECTL%" apply -f k8s/job-products.yaml -n %K8S_NAMESPACE% --validate=false'
//                 bat '"%KUBECTL%" wait --for=condition=complete job/willscot-products -n %K8S_NAMESPACE% --timeout=600s'
//             }
//         }
//     }
// }
