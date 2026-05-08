pipeline {
    agent any

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO                = '1'
        PROJECT_DIR                  = 'WillscotAutomation'
        ALLURE_RESULTS               = 'WillscotAutomation/allure-results'
        TEST_ENV                     = 'Prod'

        // Docker / Kubernetes (all local — minikube, no registry push)
        IMAGE_NAME     = 'willscot-automation'
        DOCKER_HOST    = 'tcp://127.0.0.1:2375'
        K8S_NAMESPACE  = 'willscot'
        MINIKUBE       = 'C:\\minikube\\minikube.exe'
        KUBECTL        = 'C:\\minikube\\kubectl.exe'
        // Point LocalSystem (Jenkins service) at the user-owned minikube profile and kubeconfig
        MINIKUBE_HOME  = 'C:\\Users\\santhi.podili'
        KUBECONFIG     = 'C:\\Users\\santhi.podili\\.kube\\config'

        // ── REMOTE (Docker Hub) — uncomment to re-enable remote push ──────────
        // IMAGE_TAG       = "${env.BUILD_NUMBER ?: 'latest'}"
        // DOCKER_HUB_USER = 'santhipodi'
        // DOCKER_IMAGE    = "santhipodi/willscot-automation:${env.BUILD_NUMBER ?: 'latest'}"
    }

    // ── REMOTE parameter — uncomment to re-enable push/deploy toggle ──────────
    // parameters {
    //     booleanParam(name: 'PUSH_IMAGE', defaultValue: false,
    //         description: 'Push image to Docker Hub and deploy to K8s (leave unchecked for local/CI test-only runs)')
    // }

    options {
        timestamps()
        timeout(time: 120, unit: 'MINUTES')
        buildDiscarder(logRotator(numToKeepStr: '20'))
    }

    triggers {
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
                    bat 'powershell -NonInteractive -ExecutionPolicy Bypass -File "bin\\Release\\net8.0\\playwright.ps1" install chromium'
                }
            }
        }

        // Tests run once inside the K8s container — no duplicate run here

        stage('Collect Allure Results') {
            steps {
                bat """
                    if exist "WillscotAutomation\\allure-results" rd /s /q "WillscotAutomation\\allure-results"
                    if exist "WillscotAutomation\\bin\\Release\\net8.0\\allure-results" (
                        xcopy /E /I /Y "WillscotAutomation\\bin\\Release\\net8.0\\allure-results" "WillscotAutomation\\allure-results\\"
                    )
                """
            }
        }

        stage('Publish Allure Report') {
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

        // ── Docker Build ─────────────────────────────────────────────────────
        stage('Docker Build') {
            steps {
                bat "docker build -t %IMAGE_NAME%:latest ."
            }
        }

        // ── Load image into local minikube (no registry push) ────────────────
        stage('Minikube Image Load') {
            steps {
                bat '"%MINIKUBE%" image load willscot-automation:latest'
            }
        }

        // ── REMOTE: Docker Push to Docker Hub ────────────────────────────────
        // Credential ID: dockerhub-credentials (Username/Password, user: santhipodi)
        // stage('Docker Push') {
        //     when { expression { return params.PUSH_IMAGE } }
        //     steps {
        //         withCredentials([usernamePassword(
        //             credentialsId: 'dockerhub-credentials',
        //             usernameVariable: 'DOCKER_USER',
        //             passwordVariable: 'DOCKER_PASS'
        //         )]) {
        //             bat '''
        //                 docker login -u %DOCKER_USER% -p %DOCKER_PASS%
        //                 docker push %DOCKER_IMAGE%
        //                 docker logout
        //             '''
        //         }
        //     }
        // }

        // ── Kubernetes Deploy ─────────────────────────────────────────────────
        stage('K8s Deploy') {
            // REMOTE: add  when { expression { return params.PUSH_IMAGE } }
            steps {
                // Refresh kubeconfig in case minikube API server port changed between sessions
                bat '"%MINIKUBE%" update-context'
                // --validate=false skips the OpenAPI schema download that causes TLS handshake timeouts in Jenkins/LocalSystem
                bat '"%KUBECTL%" apply -f k8s/namespace.yaml --validate=false'
                bat '"%KUBECTL%" delete job willscot-automation -n %K8S_NAMESPACE% --ignore-not-found'
                bat '"%KUBECTL%" apply -f k8s/deployment.yaml -n %K8S_NAMESPACE% --validate=false'
                // Wait for job to finish (complete=pass or failed=fail); increase timeout for container runs
                bat '"%KUBECTL%" wait --for=condition=complete job/willscot-automation -n %K8S_NAMESPACE% --timeout=1800s || "%KUBECTL%" wait --for=condition=failed job/willscot-automation -n %K8S_NAMESPACE% --timeout=60s'
            }
        }

        stage('K8s Verify') {
            // REMOTE: add  when { expression { return params.PUSH_IMAGE } }
            steps {
                bat '"%KUBECTL%" get jobs -n %K8S_NAMESPACE%'
                bat '"%KUBECTL%" get pods -n %K8S_NAMESPACE%'
                bat 'for /f "tokens=*" %%p in (\'"%KUBECTL%" get pods -n %K8S_NAMESPACE% -o name\') do "%KUBECTL%" logs %%p -n %K8S_NAMESPACE% --tail=50'
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
                cleanWhenSuccess: false,
                cleanWhenFailure: false,
                cleanWhenAborted: true
            )
        }
    }
}
