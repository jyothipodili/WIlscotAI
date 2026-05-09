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

        // Flattened kubeconfig — certs embedded, accessible by Jenkins service account.
        // Re-generate after every `minikube start`:
        // kubectl config view --raw --flatten > C:\ProgramData\Jenkins\.kube\config
        KUBECONFIG                  = 'C:\\ProgramData\\Jenkins\\.kube\\config'

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
        stage('Build & Package') {
            steps {
                dir("${PROJECT_DIR}") {
                    bat 'dotnet restore --locked-mode 2>nul || dotnet restore'
                    bat 'dotnet build --no-restore --configuration Release'
                }
                bat "docker build -t %IMAGE_NAME%:latest ."
            }
        }

        // ── 3. Minikube Image Load ───────────────────────────────────────────────
        stage('Minikube Image Load') {
            steps {
                bat '"%MINIKUBE%" image load willscot-automation:latest'
            }
        }

        // ── 4. Validate Minikube Connection ──────────────────────────────────────
        stage('Validate Minikube Connection') {
            steps {
                bat '"%MINIKUBE%" update-context'
                bat '"%KUBECTL%" config current-context'
                retry(3) {
                    bat '"%KUBECTL%" get nodes --request-timeout=30s'
                }
                bat '"%KUBECTL%" get ns'
            }
        }

        // ── 5. K8s Deploy ────────────────────────────────────────────────────────
        stage('K8s Deploy') {
            steps {
                catchError(buildResult: 'UNSTABLE', stageResult: 'FAILURE') {

                    retry(3) {
                        bat '"%KUBECTL%" apply -f k8s/namespace.yaml --validate=false'
                    }

                    bat '"%KUBECTL%" delete job willscot-automation -n %K8S_NAMESPACE% --ignore-not-found'

                    retry(3) {
                        bat '"%KUBECTL%" apply -f k8s/deployment.yaml -n %K8S_NAMESPACE% --validate=false'
                    }

                    echo 'K8s job submitted — waiting for pod to become ready...'
                    bat '"%KUBECTL%" wait --for=condition=ready pod -l job-name=willscot-automation -n %K8S_NAMESPACE% --timeout=180s'

                    bat '"%KUBECTL%" get pods -n %K8S_NAMESPACE% -l job-name=willscot-automation -o wide'
                }
            }
        }

        // ── 6. Run Tests (K8s) ───────────────────────────────────────────────────
        stage('Run Tests (K8s)') {
            steps {
                script {

                    echo "Showing pod list before wait..."
                    bat '"%KUBECTL%" get pods -n %K8S_NAMESPACE% -l job-name=willscot-automation -o wide'
                    bat(script: '"%KUBECTL%" describe job willscot-automation -n %K8S_NAMESPACE%', returnStatus: true)
                    bat(script: '"%KUBECTL%" describe pod -n %K8S_NAMESPACE% -l job-name=willscot-automation', returnStatus: true)

                    echo "Streaming logs (all pods)..."
                    bat(
                        script: '"%KUBECTL%" logs -n %K8S_NAMESPACE% -l job-name=willscot-automation --all-containers=true --tail=-1',
                        returnStatus: true
                    )

                    echo "Waiting for Job to complete or fail (max 15 min)..."
                    def rc = bat(
                        script: '"%KUBECTL%" wait job/willscot-automation -n %K8S_NAMESPACE% --for=condition=complete --for=condition=failed --timeout=900s',
                        returnStatus: true
                    )

                    echo "Showing pod list after wait..."
                    bat '"%KUBECTL%" get pods -n %K8S_NAMESPACE% -l job-name=willscot-automation -o wide'
                    bat(script: '"%KUBECTL%" describe job willscot-automation -n %K8S_NAMESPACE%', returnStatus: true)
                    bat(script: '"%KUBECTL%" describe pod -n %K8S_NAMESPACE% -l job-name=willscot-automation', returnStatus: true)

                    echo "Final logs after job finished..."
                    bat(
                        script: '"%KUBECTL%" logs -n %K8S_NAMESPACE% -l job-name=willscot-automation --all-containers=true --tail=-1',
                        returnStatus: true
                    )

                    if (rc != 0) {
                        unstable("Job did not finish within timeout OR job failed. Check logs above.")
                    } else {
                        def failRc = bat(
                            script: '"%KUBECTL%" wait --for=condition=failed job/willscot-automation -n %K8S_NAMESPACE% --timeout=5s',
                            returnStatus: true
                        )
                        if (failRc == 0) {
                            unstable("Tests failed — check Allure report.")
                        }
                    }
                }
            }
        }

        // ── 7. Collect Artifacts ─────────────────────────────────────────────────
        stage('Collect Artifacts') {
            steps {
                catchError(buildResult: 'UNSTABLE', stageResult: 'UNSTABLE') {

                    bat '''
                        if exist "allure-results" rd /s /q "allure-results"
                        if exist "TestResults"    rd /s /q "TestResults"
                        mkdir "allure-results"
                        mkdir "TestResults"
                    '''

                    bat '''
                        setlocal enabledelayedexpansion

                        for /f "delims=" %%i in ('"%KUBECTL%" get pods -n %K8S_NAMESPACE% -l job-name=willscot-automation -o jsonpath="{.items[0].metadata.name}"') do set POD=%%i

                        if "!POD!"=="" (
                            echo ERROR: No pod found for willscot-automation job
                            exit /b 1
                        )

                        echo Copying artifacts from pod: !POD!

                        "%KUBECTL%" cp %K8S_NAMESPACE%/!POD!:/app/allure-results allure-results || echo WARNING: allure-results copy incomplete
                        "%KUBECTL%" cp %K8S_NAMESPACE%/!POD!:/app/TestResults    TestResults    || echo WARNING: TestResults copy incomplete

                        endlocal
                    '''
                }
            }
        }

        // ── 8. Allure Report ─────────────────────────────────────────────────────
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

        // ── 9. Word Report ───────────────────────────────────────────────────────
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

        // ── 10. Archive Evidence ─────────────────────────────────────────────────
        stage('Archive Evidence') {
            steps {
                catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE') {
                    archiveArtifacts artifacts: 'allure-report/**',                 allowEmptyArchive: true
                    archiveArtifacts artifacts: 'allure-results/**',                allowEmptyArchive: true
                    archiveArtifacts artifacts: 'TestResults/**',                   allowEmptyArchive: true
                    archiveArtifacts artifacts: 'WillScot_ExecutiveReport_*.docx', allowEmptyArchive: true
                }
            }
        }
    }

    post {
        always {
            script {
                def status = currentBuild.result ?: 'SUCCESS'
                def icon   = status == 'SUCCESS' ? '&#9989;' : status == 'UNSTABLE' ? '&#9888;' : '&#10060;'
                currentBuild.description = """
                    ${icon} <b>WillScot Homepage Automation</b> &nbsp;|&nbsp; Env: <b>${TEST_ENV}</b> &nbsp;|&nbsp; Build: #${env.BUILD_NUMBER}<br/>
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