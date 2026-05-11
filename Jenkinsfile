pipeline {
    agent any

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO               = '1'
        IMAGE_NAME                  = 'willscot-automation'
        DOCKER_HOST                 = 'tcp://127.0.0.1:2375'
        TEST_ENV                    = 'Prod'

        ALLURE_RESULTS              = 'allure-results'
        TEST_RESULTS_DIR            = 'TestResults'
    }

    options {
        timestamps()
        timeout(time: 60, unit: 'MINUTES')
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

        // ── 2. Build Docker Image ────────────────────────────────────────────────
        stage('Build Docker Image') {
            steps {
                bat "docker build -t %IMAGE_NAME%:latest ."
            }
        }

        // ── 3. Run Tests ─────────────────────────────────────────────────────────
        stage('Run Tests') {
            steps {
                script {
                    // Clean and recreate output directories on host
                    bat '''
                        if exist "allure-results" rd /s /q "allure-results"
                        if exist "TestResults"    rd /s /q "TestResults"
                        if exist "videos"         rd /s /q "videos"
                        if exist "traces"         rd /s /q "traces"
                        mkdir "allure-results"
                        mkdir "TestResults"
                        mkdir "videos"
                        mkdir "traces"
                    '''

                    // Run tests inside Docker; volumes land artifacts on host directly
                    def rc = bat(
                        script: '''
                            docker run --rm ^
                                -e TEST_ENV=%TEST_ENV% ^
                                -e HEADLESS=true ^
                                -e BROWSER=chromium ^
                                -v "%WORKSPACE%/allure-results:/app/allure-results" ^
                                -v "%WORKSPACE%/TestResults:/app/TestResults" ^
                                -v "%WORKSPACE%/videos:/app/bin/Release/net8.0/videos" ^
                                -v "%WORKSPACE%/traces:/app/bin/Release/net8.0/traces" ^
                                %IMAGE_NAME%:latest
                        ''',
                        returnStatus: true
                    )

                    if (rc != 0) {
                        unstable("Tests finished with failures — check Allure report.")
                    }
                }
            }
        }

        // ── 4. Allure Report ─────────────────────────────────────────────────────
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

        // ── 5. Word Report ───────────────────────────────────────────────────────
        stage('Word Report') {
            steps {
                catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE') {
                    bat '''
                        set PYTHON=C:\Users\santhi.podili\AppData\Local\Programs\Python\Python313\python.exe
                        set PIP=C:\Users\santhi.podili\AppData\Local\Programs\Python\Python313\Scripts\pip.exe
                        "%PIP%" install python-docx Pillow --quiet --disable-pip-version-check
                        "%PYTHON%" scripts\\generate_word_report.py ^
                            --allure-results allure-results ^
                            --trx TestResults\\results.trx ^
                            --output "WillScot_ExecutiveReport_Build%BUILD_NUMBER%.docx" ^
                            --build %BUILD_NUMBER% ^
                            --env %TEST_ENV%
                    '''
                }
            }
        }

        // ── 6. Archive Evidence ──────────────────────────────────────────────────
        stage('Archive Evidence') {
            steps {
                catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE') {
                    archiveArtifacts artifacts: 'allure-report/**',                allowEmptyArchive: true
                    archiveArtifacts artifacts: 'allure-results/**',               allowEmptyArchive: true
                    archiveArtifacts artifacts: 'TestResults/**',                  allowEmptyArchive: true
                    archiveArtifacts artifacts: 'videos/**',                       allowEmptyArchive: true
                    archiveArtifacts artifacts: 'traces/**',                       allowEmptyArchive: true
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
                    <b>Artifacts:</b> Allure Report &bull; Videos (WebM) &bull; Screenshots &bull; Word Report
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
