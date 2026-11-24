#define _GNU_SOURCE
#include <errno.h>
#include <poll.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/prctl.h>
#include <sys/types.h>
#include <sys/syscall.h>
#include <sys/wait.h>
#include <unistd.h>

static volatile sig_atomic_t worker_group;

static void terminate(int signal_number)
{
    (void)signal_number;
    if (worker_group > 0) kill(-worker_group, SIGKILL);
    _exit(143);
}

int main(int argc, char **argv)
{
    if (argc < 2) return 64;
    pid_t parent = getppid();
    int parent_fd = (int)syscall(SYS_pidfd_open, parent, 0);
    struct sigaction action = {0};
    action.sa_handler = terminate;
    sigemptyset(&action.sa_mask);
    if (parent_fd < 0 || sigaction(SIGTERM, &action, NULL) != 0 ||
        getppid() != parent || parent == 1)
        return 70;
    sigset_t blocked;
    sigemptyset(&blocked);
    sigaddset(&blocked, SIGTERM);
    if (sigprocmask(SIG_BLOCK, &blocked, NULL) != 0) return 70;
    pid_t child = fork();
    if (child < 0) return 71;
    if (child == 0)
    {
        close(parent_fd);
        pid_t owner = getppid();
        if (setpgid(0, 0) != 0 || prctl(PR_SET_PDEATHSIG, SIGKILL) != 0 ||
            getppid() != owner || owner == 1) _exit(70);
        signal(SIGTERM, SIG_DFL);
        sigprocmask(SIG_UNBLOCK, &blocked, NULL);
        execv(argv[1], &argv[1]);
        perror("execv runtime");
        _exit(72);
    }
    worker_group = child;
    if (setpgid(child, child) != 0 && errno != EACCES)
    {
        kill(child, SIGKILL);
        return 70;
    }
    sigprocmask(SIG_UNBLOCK, &blocked, NULL);
    int status;
    for (;;)
    {
        pid_t exited = waitpid(child, &status, WNOHANG);
        if (exited == child) break;
        if (exited < 0 && errno != EINTR) return 73;
        struct pollfd owner = { .fd = parent_fd, .events = POLLIN };
        int ready = poll(&owner, 1, 25);
        if (ready > 0) terminate(SIGTERM);
        if (ready < 0 && errno != EINTR) terminate(SIGTERM);
    }
    kill(-child, SIGKILL);
    close(parent_fd);
    return WIFEXITED(status) ? WEXITSTATUS(status) : 128 + WTERMSIG(status);
}
